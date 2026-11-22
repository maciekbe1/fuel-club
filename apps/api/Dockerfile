# Build context: the repository root (docker build -f apps/api/Dockerfile .).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY dotnet-tools.json ./
COPY apps/api/FuelClub.Api.csproj apps/api/
RUN dotnet tool restore && dotnet restore apps/api
COPY apps/api/ apps/api/
RUN dotnet publish apps/api -c Release -o /app --no-restore
# Migrations as a separate executable: run it before starting a new version (the API never migrates).
# The design-time host only needs some connection string; the real one is passed when running it.
RUN ConnectionStrings__DefaultConnection="Host=unused" \
    dotnet tool run dotnet-ef migrations bundle --project apps/api --configuration Release -o /app/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Npgsql probes GSSAPI at connect; without the library it logs an error on every start.
# tini as PID 1: a failed start (unhandled exception -> abort) must end the container, and PID 1
# without a handler ignores that signal - the container stayed "Up" with a dead application.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 tini \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["tini", "--", "dotnet", "FuelClub.Api.dll"]
