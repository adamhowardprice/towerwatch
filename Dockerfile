# Build toolchain for TowerArchers. The host needs only Docker.
FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends unzip zip curl \
    && rm -rf /var/lib/apt/lists/*

# DepotDownloader fetches the Valheim dedicated server with an anonymous Steam login.
ARG DEPOT_DOWNLOADER_VERSION=3.4.0
ARG TARGETARCH
RUN case "${TARGETARCH:-$(dpkg --print-architecture)}" in \
        arm64) arch=arm64 ;; \
        amd64) arch=x64 ;; \
        *) echo "Unsupported architecture" >&2; exit 1 ;; \
    esac \
    && curl -fsSL -o /tmp/dd.zip "https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_${DEPOT_DOWNLOADER_VERSION}/DepotDownloader-linux-${arch}.zip" \
    && unzip -q /tmp/dd.zip -d /opt/depotdownloader \
    && chmod +x /opt/depotdownloader/DepotDownloader \
    && ln -s /opt/depotdownloader/DepotDownloader /usr/local/bin/DepotDownloader \
    && rm /tmp/dd.zip

ARG ILSPYCMD_VERSION=11.2.0.10050
RUN dotnet tool install --tool-path /usr/local/bin ilspycmd --version "${ILSPYCMD_VERSION}"

WORKDIR /work
