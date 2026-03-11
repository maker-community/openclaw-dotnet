# Build context must be the repo root:
#   docker build -f OpenClaw.McpServer/Dockerfile -t openclaw-mcpserver .

# ─── Stage 1: build ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first (layer-cache friendly)
COPY OpenClaw.GatewayClient/OpenClaw.GatewayClient.csproj OpenClaw.GatewayClient/
COPY OpenClaw.McpServer/OpenClaw.McpServer.csproj          OpenClaw.McpServer/

RUN dotnet restore OpenClaw.McpServer/OpenClaw.McpServer.csproj

# Copy full source
COPY OpenClaw.GatewayClient/ OpenClaw.GatewayClient/
COPY OpenClaw.McpServer/     OpenClaw.McpServer/

RUN dotnet publish OpenClaw.McpServer/OpenClaw.McpServer.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ─── Stage 2: runtime ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# libsodium native binaries are bundled in the NuGet package;
# no extra apt installs required.

COPY --from=build /app/publish .

# MCP server listens on HTTP only (no TLS termination in container)
ENV ASPNETCORE_URLS=http://+:5223
ENV ASPNETCORE_ENVIRONMENT=Production

# Gateway URL — override at runtime, e.g.:
#   -e OpenClaw__GatewayUrl=ws://host.docker.internal:18789
#   -e OpenClaw__Token=your-token
#   -e OpenClaw__ApiKey=your-api-key
ENV OpenClaw__GatewayUrl=ws://host.docker.internal:18789

EXPOSE 5223

ENTRYPOINT ["dotnet", "OpenClaw.McpServer.dll"]
