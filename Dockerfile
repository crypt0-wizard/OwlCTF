# syntax=docker/dockerfile:1.7

FROM caddy:2.11.4-alpine AS caddy
COPY Caddyfile /etc/caddy/Caddyfile

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/OwlCTF.Web/OwlCTF.Web.csproj src/OwlCTF.Web/
RUN --mount=type=cache,id=owlctf-nuget,target=/root/.nuget/packages \
    dotnet restore src/OwlCTF.Web/OwlCTF.Web.csproj

COPY src/OwlCTF.Web/ src/OwlCTF.Web/
RUN --mount=type=cache,id=owlctf-nuget,target=/root/.nuget/packages \
    dotnet publish src/OwlCTF.Web/OwlCTF.Web.csproj \
      --configuration Release \
      --output /out \
      --no-restore \
      /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data/challenge-files /app/data/keys /app/wwwroot/uploads \
    && chown -R "$APP_UID:$APP_UID" /app/data /app/wwwroot/uploads

COPY --from=build --chown=$APP_UID:$APP_UID /out/ ./

USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

HEALTHCHECK --interval=15s --timeout=3s --start-period=45s --retries=4 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health/ready >/dev/null || exit 1

ENTRYPOINT ["dotnet", "OwlCTF.dll"]
