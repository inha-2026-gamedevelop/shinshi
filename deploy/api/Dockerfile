# Build from the repository root: docker build -f deploy/api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Preserve the relative path used by Shinshi.Contracts' linked Unity DTO sources.
COPY Backend/ Backend/
COPY Assets/01.Scripts/00.Common/Contracts/ Assets/01.Scripts/00.Common/Contracts/

RUN dotnet restore Backend/Shinshi.Backend.slnx \
    --configfile Backend/NuGet.Config
RUN dotnet build Backend/Shinshi.Backend.slnx \
    --configuration Release --no-restore
RUN dotnet publish Backend/src/Shinshi.Api/Shinshi.Api.csproj \
    --configuration Release --no-build --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish/ ./
# shinshi-json writes the deploy/LOGGING.md common keys (UTC ts, level, service=api, ...). Set Observability__InstanceId,
# Observability__BuildId and Observability__ContentVersion at run time; unset values are logged as null.
# Framework request logs are left at Warning because api.request.completed already records each request.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    Logging__Console__FormatterName=shinshi-json \
    Logging__LogLevel__Default=Information \
    Logging__LogLevel__Microsoft.AspNetCore=Warning

EXPOSE 8080/tcp
# The container check uses /health/live (process answers, no database access) on purpose: if it used /health/ready, a database
# outage would mark the container unhealthy and any auto-heal or orchestrator that restarts unhealthy containers would restart
# a healthy API against a down database. Restarting does not fix the database, drops in-flight requests and settlement work,
# and adds a reconnect storm. Database readiness is watched from outside: Prometheus probes /health/ready (ShinshiApiNotReady).
# APP_UID is the non-root app user supplied by the official .NET Linux image.
USER $APP_UID
STOPSIGNAL SIGTERM
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD ["curl", "--fail", "--silent", "--show-error", "--noproxy", "*", "--connect-timeout", "2", "--max-time", "3", "--output", "/dev/null", "http://127.0.0.1:8080/health/live"]
ENTRYPOINT ["dotnet", "Shinshi.Api.dll"]
