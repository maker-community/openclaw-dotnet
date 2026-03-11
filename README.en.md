# OpenClaw .NET Client + Proxy API + Dashboard Lite

[中文版](README.md)

## Purpose
- Connect to OpenClaw Gateway (`ws://127.0.0.1:18789`) with .NET.
- Provide separated frontend/backend:
  - Proxy API (REST + SignalR bridge)
  - Lightweight Blazor WASM UI

> This repository currently provides a generic pass-through endpoint (`/api/openclaw/call`) first.
> Strongly typed endpoints for common methods (chat.send / tools.invoke / sessions.list) can be added later.

## Project Structure
- `OpenClaw.GatewayClient/`: WebSocket protocol client (`connect.challenge -> connect -> hello-ok`, req/res/event frames)
- `OpenClaw.ProxyApi/`: REST + SignalR (`/hub/openclaw`) and gateway event broadcast
- `OpenClaw.DashboardLite/`: Blazor WASM + MudBlazor UI (`/gateway` page for generic method calls + events)

## .NET Setup and Run from Source

### Prerequisites

- `.NET SDK 10.0` (required)
- A running OpenClaw Gateway (default `ws://127.0.0.1:18789`)

### Install .NET SDK 10.0

#### Windows

**Option 1 — winget (recommended)**
```powershell
winget install Microsoft.DotNet.SDK.10
```

**Option 2 — Chocolatey**
```powershell
choco install dotnet-sdk --version=10.0.0
```

**Option 3 — Official installer**  
Download and run the `.exe` installer from https://dotnet.microsoft.com/download/dotnet/10.0.

**Option 4 — Visual Studio 2022 (bundled)**  
Select the ".NET desktop development" or "ASP.NET and web development" workload during installation; the SDK is included automatically.

#### macOS

**Option 1 — Official pkg installer**  
Download and run the `.pkg` package from https://dotnet.microsoft.com/download/dotnet/10.0.

**Option 2 — Homebrew**
```bash
brew install --cask dotnet-sdk
```

**Option 3 — install script**
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0
```
Then add to `PATH`:
```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools
```

#### Linux

**Ubuntu / Debian**
```bash
# Works out-of-the-box on Ubuntu 22.04 / 24.04 with Microsoft package feed
apt-get update
apt-get install -y dotnet-sdk-10.0
```
If the package is not yet available in your distro's feed, add the Microsoft feed first:
```bash
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
dpkg -i packages-microsoft-prod.deb
apt-get update
apt-get install -y dotnet-sdk-10.0
```

**Fedora / RHEL / CentOS Stream**
```bash
dnf install dotnet-sdk-10.0
```

**Arch Linux (AUR)**
```bash
yay -S dotnet-sdk-bin
```

**Generic install script (any distro)**
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools
```

### Verify Installation

```bash
dotnet --version
```

Continue if output is `10.x`.

### Run from Source (recommended)

```bash
# 1) Go to repository root
cd openclaw-dotnet

# 2) Restore dependencies
dotnet restore

# 3) Start ProxyApi
dotnet run --project OpenClaw.ProxyApi/OpenClaw.ProxyApi.csproj

# 4) Start DashboardLite in another terminal (optional)
dotnet run --project OpenClaw.DashboardLite/OpenClaw.DashboardLite.csproj

# 5) Or start McpServer
dotnet run --project OpenClaw.McpServer/OpenClaw.McpServer.csproj
```

### Local Config Files

- `OpenClaw.ProxyApi/appsettings.Development.json`
- `OpenClaw.McpServer/appsettings.Development.json`

At minimum, configure:
- `OpenClaw:GatewayUrl`
- `OpenClaw:Token`
- `OpenClaw:ApiKey`

## Local Run

### 1) Configure ProxyApi

Edit `OpenClaw.ProxyApi/appsettings.Development.json`:

```json
{
  "OpenClaw": {
    "GatewayUrl": "ws://127.0.0.1:18789",
    "Token": "<your gateway token>",
    "ApiKey": "<your-api-key>"
  }
}
```

- Leave `ApiKey` empty to disable API-key protection.
- If enabled, requests must include header: `X-Api-Key: dev`.

### 2) Start ProxyApi

```bash
cd OpenClaw.ProxyApi
DOTNET_ENVIRONMENT=Development dotnet run
```

Swagger: `http://localhost:5xxx/swagger`

### 3) Start DashboardLite

```bash
cd OpenClaw.DashboardLite
dotnet run
```

Pages:
- `/` home
- `/gateway` SignalR events + method call panel

## API

### POST /api/openclaw/call

Request body:

```json
{
  "method": "sessions.list",
  "params": {}
}
```

Response:

```json
{ "ok": true, "payload": {} }
```

## Docker — McpServer

Image: `gilzhang/verdure-openclaw-mcpserver:v0.1.0`

### Build and Push

```bash
# Build (from repository root)
docker build -f OpenClaw.McpServer/Dockerfile \
  -t gilzhang/verdure-openclaw-mcpserver:v0.1.0 \
  -t gilzhang/verdure-openclaw-mcpserver:latest \
  .

# Push
docker push gilzhang/verdure-openclaw-mcpserver:v0.1.0
docker push gilzhang/verdure-openclaw-mcpserver:latest
```

### Run on Server

> The OpenClaw Gateway only listens on `127.0.0.1:18789` and is not exposed externally. Bridge-network containers cannot reach it via `host.docker.internal`, so use `--network host` to connect directly to the host's localhost.

```bash
docker run -d \
  --name verdure-openclaw-mcpserver \
  --network host \
  -e ASPNETCORE_URLS=http://0.0.0.0:3001 \
  -e OpenClaw__GatewayUrl=ws://127.0.0.1:18789 \
  -e OpenClaw__Token=<your-token> \
  -e OpenClaw__ApiKey=<your-api-key> \
  --restart unless-stopped \
  gilzhang/verdure-openclaw-mcpserver:v0.1.0
```

> Do **not** add `-p` port mappings with `--network host`. MCP endpoint: `http://<host>:3001/mcp`.  
> Host networking is effective on Linux hosts only. On Windows / macOS Docker Desktop, run directly from source instead.

## Xiaozhi MCP Relay Integration

Reference: `maker-community/verdure-mcp-for-xiaozhi`

Use this service as an MCP endpoint in Xiaozhi relay platform:

### Platform-side Configuration
- MCP endpoint: `http://<your-server-ip>:3001/mcp`
- Auth method: HTTP Header
- Header key: `X-Api-Key`
- Header value: same as `OpenClaw__ApiKey`

### Recommended Steps
1. Start the container on your server.
2. Verify endpoint reachability: `curl http://127.0.0.1:<port>/mcp`.
3. Add MCP service in Xiaozhi platform using endpoint + header.
4. Bind MCP service to your target Xiaozhi node.
5. Validate with tool calls (`health`, `sessions.list`, etc.).

### Troubleshooting
- `connection refused (host.docker.internal:18789)`: use host-network mode if gateway is localhost-only.
- `401 Unauthorized`: check `X-Api-Key` and `OpenClaw__ApiKey` consistency.
- Pairing/token errors: check `OpenClaw__Token` and gateway-side pairing state.

### Example Screenshots (Placeholder)

> Replace these placeholder images with real screenshots from your production deployment.

![Placeholder: Xiaozhi MCP service configuration page](docs/images/placeholder-xiaozhi-config.png)
![Placeholder: Container running and logs](docs/images/placeholder-container-logs.png)
![Placeholder: Successful tool invocation in Xiaozhi](docs/images/placeholder-tool-call-success.png)

## Notes
- Current `OpenClawDefaults.ProtocolVersion = 1`. If your gateway protocol is not version 1, sync `PROTOCOL_VERSION` from OpenClaw source.
- Current auth implementation is token-based. Device signature/device token logic is partially implemented and may require gateway-side pairing.
