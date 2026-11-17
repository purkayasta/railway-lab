FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source

COPY deploy/auth/ deploy/auth/
COPY src/Platform/ src/Platform/
COPY src/Gateway/ src/Gateway/
RUN dotnet restore src/Gateway/Gateway.csproj
RUN dotnet publish src/Gateway/Gateway.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
LABEL org.railway-lab.project="railway-lab" org.railway-lab.service="gateway"
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Gateway.dll"]
