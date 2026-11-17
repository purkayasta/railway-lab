FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source

COPY deploy/auth/ deploy/auth/
COPY src/Platform/ src/Platform/
COPY src/Services/Auth/ src/Services/Auth/
RUN dotnet restore src/Services/Auth/Auth.csproj
RUN dotnet publish src/Services/Auth/Auth.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
LABEL org.railway-lab.project="railway-lab" org.railway-lab.service="auth"
WORKDIR /app

# Microsoft.Data.SqlClient requires ICU on Linux/Alpine to support non-invariant globalization.
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

COPY --from=build /app/publish .

# Token signing and encryption keys live on a volume so tokens survive restarts.
RUN mkdir /keys && chown $APP_UID /keys

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Auth.dll"]
