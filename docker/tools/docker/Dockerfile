# ============================================================
# ShapeRise Arena – Dedicated Headless Linux Server
# Unity 6 Mono build → Ubuntu 22.04 minimal runtime
# Build context: project root (docker build -f docker/Dockerfile .)
# ============================================================

FROM ubuntu:22.04 AS runtime

LABEL maintainer="ShapeRise Team"
LABEL description="ShapeRise Arena dedicated headless server"

# Avoid interactive prompts during apt
ENV DEBIAN_FRONTEND=noninteractive

# Runtime dependencies for Unity Mono headless
RUN apt-get update && apt-get install -y --no-install-recommends \
        libglib2.0-0 \
        libc6 \
        ca-certificates \
        procps \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user (OWASP: principle of least privilege)
RUN groupadd -r shaperise && useradd -r -g shaperise -u 1000 shaperise

WORKDIR /app

# Copy pre-built Unity headless server from server-build/
COPY server-build/ ./

# Ensure the server binary is executable
RUN chmod +x ./ShapeRiseServer.x86_64

# Create log directory owned by non-root user
RUN mkdir -p /app/logs && chown -R shaperise:shaperise /app

# Switch to non-root user
USER shaperise

# KCP Transport: UDP 7777 (Mirror default)
EXPOSE 7777/udp

# Health check: verify process is running
HEALTHCHECK --interval=30s --timeout=10s --start-period=20s --retries=3 \
    CMD pgrep -x ShapeRiseServer > /dev/null || exit 1

ENTRYPOINT ["./ShapeRiseServer.x86_64", \
    "-batchmode", \
    "-nographics", \
    "-logFile", "/app/logs/server.log"]
