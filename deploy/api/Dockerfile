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
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    Logging__Console__FormatterName=json \
    Logging__Console__FormatterOptions__IncludeScopes=true \
    Logging__Console__FormatterOptions__UseUtcTimestamp=true \
    Logging__Console__FormatterOptions__TimestampFormat=yyyy-MM-ddTHH:mm:ss.fffZ

EXPOSE 8080/tcp
# APP_UID is the non-root app user supplied by the official .NET Linux image.
USER $APP_UID
STOPSIGNAL SIGTERM
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD ["curl", "--fail", "--silent", "--show-error", "--noproxy", "*", "--connect-timeout", "2", "--max-time", "3", "--output", "/dev/null", "http://127.0.0.1:8080/v1/service-state"]
ENTRYPOINT ["dotnet", "Shinshi.Api.dll"]
