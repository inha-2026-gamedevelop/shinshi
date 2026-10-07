# Build from the repository root: docker build -f deploy/admin-web/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Preserve the linked Unity DTO paths used by Shinshi.Contracts.
COPY Backend/ Backend/
COPY Assets/01.Scripts/00.Common/Contracts/ Assets/01.Scripts/00.Common/Contracts/

RUN dotnet restore Backend/src/Shinshi.Admin.Web/Shinshi.Admin.Web.csproj \
    --configfile Backend/NuGet.Config
RUN dotnet publish Backend/src/Shinshi.Admin.Web/Shinshi.Admin.Web.csproj \
    --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish/ ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    Logging__LogLevel__Default=Information \
    Logging__LogLevel__Microsoft.AspNetCore=Warning

EXPOSE 8080/tcp
USER $APP_UID
STOPSIGNAL SIGTERM
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD ["curl", "--fail", "--silent", "--show-error", "--noproxy", "*", "--connect-timeout", "2", "--max-time", "3", "--output", "/dev/null", "http://127.0.0.1:8080/healthz"]
ENTRYPOINT ["dotnet", "Shinshi.Admin.Web.dll"]