# syntax=docker/dockerfile:1
# 构建阶段：只用 mcr 镜像（Docker Hub 在国内常不可达）
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/MicaRomManager.Core/MicaRomManager.Core.csproj src/MicaRomManager.Core/
COPY src/MicaRomManager.Web/MicaRomManager.Web.csproj src/MicaRomManager.Web/
RUN dotnet restore src/MicaRomManager.Web/MicaRomManager.Web.csproj

COPY src/ src/
RUN dotnet publish src/MicaRomManager.Web/MicaRomManager.Web.csproj \
    -c Release -o /app /p:UseAppHost=false

# 运行阶段（.NET 10 默认 Debian 基础镜像，含 bash/setpriv）
FROM mcr.microsoft.com/dotnet/aspnet:10.0

LABEL org.opencontainers.image.title="Mica ROM Manager" \
      org.opencontainers.image.description="Web/Docker ROM manager with scanning, dedupe, metadata and AI scraping" \
      org.opencontainers.image.licenses="MIT"

ENV ASPNETCORE_URLS=http://+:8080 \
    MICA_DATA_DIR=/data \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    TZ=Asia/Shanghai \
    HOME=/tmp

WORKDIR /app
COPY --from=build /app .

COPY deploy/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod 0755 /usr/local/bin/entrypoint.sh

VOLUME ["/data"]
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["/bin/bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080"]

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
