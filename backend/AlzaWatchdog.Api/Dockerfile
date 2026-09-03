# Build from the repository root:
#   podman build -f backend/AlzaWatchdog.Api/Dockerfile -t alza-watchdog-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the project files alone so this layer survives source edits.
COPY backend/AlzaWatchdog.Api/AlzaWatchdog.Api.csproj backend/AlzaWatchdog.Api/
RUN dotnet restore backend/AlzaWatchdog.Api/AlzaWatchdog.Api.csproj

COPY backend/AlzaWatchdog.Api/ backend/AlzaWatchdog.Api/
RUN dotnet publish backend/AlzaWatchdog.Api/AlzaWatchdog.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# The SQLite file lives here so a volume can outlive the container.
RUN mkdir -p /data && chown app:app /data
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Default="Data Source=/data/alzawatchdog.db"

EXPOSE 8080
ENTRYPOINT ["dotnet", "AlzaWatchdog.Api.dll"]
