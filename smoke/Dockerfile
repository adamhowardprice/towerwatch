# Runtime for the x86-64 Valheim dedicated server. On Apple Silicon this runs under emulation.
FROM --platform=linux/amd64 ubuntu:24.04
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl unzip libatomic1 libpulse0 libpulse-mainloop-glib0 libc6 libstdc++6 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /work
