# ============================================================
# VRCGT v2 -- Docker multi-stage build
# ============================================================
#
# Stage 1 : node:20-alpine  -- builds the React frontend
# Stage 2 : dotnet/sdk:8.0  -- publishes VRCGT.Server
# Stage 3 : dotnet/aspnet:8.0 -- minimal runtime image
#
# Build from the repo root:
#   docker build -f docker/Dockerfile -t vrcgt:latest .
#
# Or use docker compose:
#   docker compose -f docker/docker-compose.yml up
# ============================================================

# ---- Stage 1: Build React frontend ------------------------------------------
FROM node:20-alpine AS frontend

WORKDIR /src

# Install deps before copying source so the layer is cached across code changes
COPY src/VRCGT.Web/package*.json ./
RUN npm ci --prefer-offline

COPY src/VRCGT.Web/ ./
RUN npm run build
# Output: /src/dist/

# ---- Stage 2: Publish .NET server -------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS dotnet-build

WORKDIR /repo

# Copy solution file and project files first so restore is cached separately
# from the source copy.  Only the projects that VRCGT.Server depends on are
# needed; VRCGT.Launcher and the legacy WPF project are excluded.
COPY VRCGroupTools.sln                       ./
COPY src/VRCGT.Core/VRCGT.Core.csproj        src/VRCGT.Core/
COPY src/VRCGT.Infrastructure/VRCGT.Infrastructure.csproj \
                                              src/VRCGT.Infrastructure/
COPY src/VRCGT.Server/VRCGT.Server.csproj    src/VRCGT.Server/

# Restore without compiling -- cached separately from source
RUN dotnet restore src/VRCGT.Server/VRCGT.Server.csproj \
    --runtime linux-x64 \
    -p:SkipWebFrontend=true

# Copy the rest of the source
COPY src/VRCGT.Core/           src/VRCGT.Core/
COPY src/VRCGT.Infrastructure/ src/VRCGT.Infrastructure/
COPY src/VRCGT.Server/         src/VRCGT.Server/

# Inject the pre-built React assets into wwwroot/
# The server's BuildWebFrontend MSBuild target is skipped so Node is not needed
COPY --from=frontend /src/dist/ src/VRCGT.Server/wwwroot/

# Publish -- framework-dependent so the base image supplies the managed runtime.
# SkipWebFrontend=true: the npm build already ran above, don't run it again.
RUN dotnet publish src/VRCGT.Server/VRCGT.Server.csproj \
    --configuration Release \
    /p:SkipWebFrontend=true \
    --output /app/publish \
    --nologo

# ---- Stage 3: Runtime image -------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Create a non-root user for the app process
RUN useradd -r -u 1001 -g root vrcgt

WORKDIR /app

# Copy published artifacts from the build stage
COPY --from=dotnet-build /app/publish ./

# The /app/data directory is the default persistent data volume mount point.
# Its ownership is set here; the actual content is provided by a bind-mount or
# named volume at runtime, so nothing sensitive is baked into the image.
RUN mkdir -p /app/data && chown -R vrcgt:root /app/data && chmod -R g+rw /app/data

# Run as non-root
USER vrcgt

# ---- Environment defaults ---------------------------------------------------
# VRCGT_DATA_DIR  -- where the SQLite database, logs, cache, and exports land
# VRCGT_BIND_URL  -- Kestrel listen URL; 0.0.0.0 so Docker port-mapping works
# ASPNETCORE_ENVIRONMENT -- suppress development-only middleware in the image
ENV VRCGT_DATA_DIR=/app/data
ENV VRCGT_BIND_URL=http://0.0.0.0:45721
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_NOLOGO=1

# EXPOSE declares the container-side port; the host-side mapping is set in
# docker-compose.yml (or with -p on the docker run command line).
EXPOSE 45721

# ---- Healthcheck ------------------------------------------------------------
# The /health endpoint is unauthenticated and returns {"status":"ok"}.
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -qO- http://localhost:45721/health || exit 1

# ---- Entry point ------------------------------------------------------------
ENTRYPOINT ["dotnet", "VRCGT.Server.dll"]
