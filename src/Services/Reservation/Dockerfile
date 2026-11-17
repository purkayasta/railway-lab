FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source
COPY deploy/auth/ deploy/auth/
COPY src/Platform/ src/Platform/
COPY src/Services/Reservation/ src/Services/Reservation/
RUN dotnet restore src/Services/Reservation/Reservation.csproj
RUN dotnet publish src/Services/Reservation/Reservation.csproj -c Release --no-restore -o /app/publish
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
LABEL org.railway-lab.project="railway-lab" org.railway-lab.service="reservation"
WORKDIR /app
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Reservation.dll"]
