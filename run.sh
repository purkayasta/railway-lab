#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$root_dir"

usage() {
    cat <<'HELP'
Usage: ./run.sh [local|init-db|docker|stop|clean|help]
  local    Start SQL Server in Docker, then run .NET and Angular locally (default).
  init-db  Start SQL Server in Docker and initialize service schemas/logins.
  docker   Build and start the full Docker Compose stack.
  stop     Stop the Docker Compose stack without removing data.
  clean    Remove Compose containers, volumes and app images after confirmation.
HELP
}

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        printf 'Required command is missing: %s\n' "$1" >&2
        exit 1
    fi
}

mode="${1:-local}"
if [[ $# -gt 1 ]]; then
    usage >&2
    exit 2
fi
case "$mode" in
    help|--help|-h) usage; exit 0 ;;
    local|init-db|docker|stop|clean) ;;
    *) usage >&2; exit 2 ;;
esac

if [[ ! -f .env ]]; then
    printf '%s\n' 'Create .env from .env.example and configure its values first.' >&2
    exit 1
fi

require_command docker
compose=(docker compose --project-name railway-lab --env-file "$root_dir/.env" -f "$root_dir/docker-compose.yml")

case "$mode" in
    docker|stop|clean)
        case "$mode" in
            docker) exec "${compose[@]}" up --build -d ;;
            stop) exec "${compose[@]}" stop ;;
            clean)
                printf '%s\n' 'This removes the Compose database, Auth keys, containers and locally built app images.'
                read -r -p 'Type clean to continue: ' confirmation
                if [[ "$confirmation" != clean ]]; then
                    printf '%s\n' 'Cancelled.'
                    exit 1
                fi
                "${compose[@]}" down --volumes --remove-orphans
                for service in auth schedule reservation gateway web; do
                    image="railway-lab-${service}:latest"
                    if [[ -n "$(docker image ls --quiet --filter "reference=$image")" ]]; then
                        docker image rm "$image"
                    fi
                done
                exit 0
                ;;
        esac
        ;;
esac

# Compose parses .env as data; never source/eval credentials as shell code.
# Use the same resolved values for the SQL container and local applications.
if ! local_environment="$("${compose[@]}" config --environment)"; then
    printf '%s\n' 'Could not read the Compose environment from .env.' >&2
    exit 1
fi
while IFS='=' read -r key value; do
    case "$key" in
        MSSQL_SA_PASSWORD|AUTH_DB_PASSWORD|SCHEDULE_DB_PASSWORD|RESERVATION_DB_PASSWORD|PAYMENT_DB_PASSWORD|ANALYTICS_DB_PASSWORD|HELPDESK_DB_PASSWORD|SEED_USER_PASSWORD|INTERNAL_SERVICES_CLIENT_SECRET)
            printf -v "$key" '%s' "$value"
            export -n "$key"
            ;;
    esac
done <<< "$local_environment"
unset local_environment key value

start_database() (
    export MSSQL_SA_PASSWORD AUTH_DB_PASSWORD SCHEDULE_DB_PASSWORD RESERVATION_DB_PASSWORD PAYMENT_DB_PASSWORD ANALYTICS_DB_PASSWORD HELPDESK_DB_PASSWORD SEED_USER_PASSWORD INTERNAL_SERVICES_CLIENT_SECRET
    printf '%s\n' 'Starting SQL Server in Docker and initializing service databases...'
    "${compose[@]}" up -d --wait sqlserver
    "${compose[@]}" run --rm --no-deps db-init
)

if [[ "$mode" == init-db ]]; then
    start_database
    exit 0
fi

require_command node
require_command curl
if [[ ! -f src/Web/node_modules/@angular/cli/bin/ng.js ]]; then
    printf '%s\n' 'Install web dependencies first: npm --prefix src/Web ci' >&2
    exit 1
fi

supports_dotnet10() {
    [[ -n "$1" ]] && "$1" --list-sdks | grep -q '^10\.' &&
        "$1" --list-runtimes | grep -q '^Microsoft.AspNetCore.App 10\.'
}
dotnet_bin="${DOTNET_BIN:-$(command -v dotnet || true)}"
if ! supports_dotnet10 "$dotnet_bin"; then
    if [[ -z "${DOTNET_BIN:-}" ]] && supports_dotnet10 "$HOME/.dotnet/dotnet"; then
        dotnet_bin="$HOME/.dotnet/dotnet"
    else
        printf '%s\n' 'Install the .NET 10 SDK/runtime or set DOTNET_BIN to its dotnet executable.' >&2
        exit 1
    fi
fi

# Fail before launching anything if a previous stack already owns the app ports.
node <<'JS'
const net = require('node:net');
(async () => {
  for (const port of [8180, 5101, 5102, 8080, 4200]) {
    await new Promise((resolve, reject) => {
      const server = net.createServer();
      server.once('error', error => reject(new Error(error.code === 'EADDRINUSE'
        ? `Port ${port} is in use. Stop the previous app/Compose stack first.`
        : `Cannot check port ${port}: ${error.message}`)));
      server.listen(port, '127.0.0.1', () => server.close(resolve));
    });
  }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
JS

connection_string() {
    local service="$1" password="$2" server="localhost,1433"
    password="${password//\"/\"\"}"
    server="${server//\"/\"\"}"
    printf 'Server="%s";Database=RailwayLab;User ID=%s_svc;Password="%s";Encrypt=True;TrustServerCertificate=True' "$server" "$service" "$password"
}

# Bash 3.2 requires a sentinel for an empty array with nounset enabled.
local_pids=("")
cleanup() {
    local pid
    for pid in "${local_pids[@]}"; do
        [[ -n "$pid" ]] || continue
        kill "$pid" 2>/dev/null || true
    done
    for pid in "${local_pids[@]}"; do
        [[ -n "$pid" ]] || continue
        wait "$pid" 2>/dev/null || true
    done
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

start_database

printf '%s\n' 'Building the solution for local development...'
"$dotnet_bin" build RailwayLab.sln
umask 077
mkdir -p "$root_dir/.local-dev/auth-keys"

start_service() {
    local name="$1" directory="$2" assembly="$3" port="$4" connection="${5:-}"
    (
        cd "$root_dir/$directory"
        export ASPNETCORE_ENVIRONMENT=Development
        export ASPNETCORE_URLS="http://localhost:$port"
        export Auth__Authority=http://localhost:8180
        export Auth__Issuer=http://localhost:8180/
        export Auth__RequireHttpsMetadata=false
        export Schedule__BaseAddress=http://localhost:5101/
        export Reservation__BaseAddress=http://localhost:5102/
        if [[ "$name" == Auth ]]; then
            export Auth__WebOrigin=http://localhost:4200
            export Auth__KeyDirectory="$root_dir/.local-dev/auth-keys"
            export Auth__SeedUserPassword="$SEED_USER_PASSWORD"
            export Auth__InternalServicesClientSecret="$INTERNAL_SERVICES_CLIENT_SECRET"
        fi
        if [[ -n "$connection" ]]; then
            export ConnectionStrings__Default="$connection"
        fi
        exec "$dotnet_bin" "bin/Debug/net10.0/$assembly.dll"
    ) &
    local_pids+=("$!")
    printf 'Started %s (PID %s)\n' "$name" "$!"
}

wait_for_service() {
    local name="$1" port="$2" pid="$3" attempt
    for ((attempt = 0; attempt < 120; attempt++)); do
        if ! kill -0 "$pid" 2>/dev/null; then
            printf '%s stopped before becoming ready. Check the application error and docker compose logs sqlserver.\n' "$name" >&2
            return 1
        fi
        if curl --silent --fail --max-time 2 "http://localhost:$port/health/ready" >/dev/null; then
            return 0
        fi
        sleep 1
    done
    printf 'Timed out waiting for %s on port %s.\n' "$name" "$port" >&2
    return 1
}

start_service Auth src/Services/Auth Auth 8180 "$(connection_string auth "$AUTH_DB_PASSWORD")"
wait_for_service Auth 8180 "${local_pids[${#local_pids[@]}-1]}"
unset SEED_USER_PASSWORD INTERNAL_SERVICES_CLIENT_SECRET
start_service Schedule src/Services/Schedule Schedule 5101 "$(connection_string schedule "$SCHEDULE_DB_PASSWORD")"
wait_for_service Schedule 5101 "${local_pids[${#local_pids[@]}-1]}"
start_service Reservation src/Services/Reservation Reservation 5102 "$(connection_string reservation "$RESERVATION_DB_PASSWORD")"
wait_for_service Reservation 5102 "${local_pids[${#local_pids[@]}-1]}"
start_service Gateway src/Gateway Gateway 8080
wait_for_service Gateway 8080 "${local_pids[${#local_pids[@]}-1]}"

(
    cd "$root_dir/src/Web"
    exec node node_modules/@angular/cli/bin/ng.js serve --proxy-config proxy.conf.json --host 127.0.0.1 --port 4200
) &
local_pids+=("$!")
printf '%s\n' 'Angular is starting at http://localhost:4200. Press Ctrl-C to stop the local apps; SQL Server is left running.'
while true; do
    for pid in "${local_pids[@]}"; do
        [[ -n "$pid" ]] || continue
        if ! kill -0 "$pid" 2>/dev/null; then
            printf 'Local process %s stopped; shutting down the remaining apps.\n' "$pid" >&2
            exit 1
        fi
    done
    sleep 1
done
