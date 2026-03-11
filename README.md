# OpenClaw .NET Client + Proxy API + Dashboard Lite

[English Version](README.en.md)

## 目标
- 用 .NET 连接 OpenClaw Gateway (`ws://127.0.0.1:18789`)。
- 对外提供前后端分离：
  - Proxy API（REST + SignalR 事件桥接）
  - Blazor WASM + MudBlazor 的轻量 UI

> 当前实现先提供“全量 methods 的通用调用通道” `/api/openclaw/call`。
> 后续可以按需把常用 methods（chat.send / tools.invoke / sessions.list 等）做成强类型 DTO + 独立端点。

## 目录
- `OpenClaw.GatewayClient/`：WS 协议客户端（connect.challenge → connect → hello-ok；req/res/event frames）
- `OpenClaw.ProxyApi/`：对外 REST + SignalR (`/hub/openclaw`)；把 gateway event 广播给前端
- `OpenClaw.DashboardLite/`：Blazor WASM + MudBlazor UI（`/gateway` 页面可直接调用 method + 看 events）

## .NET 环境安装与源码运行

### 前置要求

- `.NET SDK 10.0`（必须）
- 可运行的 OpenClaw Gateway（默认 `ws://127.0.0.1:18789`）

### 安装 .NET SDK 10.0

#### Windows

**方式一：winget（推荐）**
```powershell
winget install Microsoft.DotNet.SDK.10
```

**方式二：Chocolatey**
```powershell
choco install dotnet-sdk --version=10.0.0
```

**方式三：官方安装包**  
前往 https://dotnet.microsoft.com/download/dotnet/10.0 下载 `.exe` 安装包并运行。

**方式四：Visual Studio 2022（附带）**  
安装 Visual Studio 2022 时勾选 ".NET 桌面开发" 或 "ASP.NET 和 Web 开发" 工作负载，将自动安装对应 SDK。

#### macOS

**方式一：官方安装包**  
前往 https://dotnet.microsoft.com/download/dotnet/10.0 下载 `.pkg` 安装包并运行。

**方式二：Homebrew**
```bash
brew install --cask dotnet-sdk
```

**方式三：install 脚本**
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0
```
执行后将 `~/.dotnet` 添加到 `PATH`：
```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools
```

#### Linux

**Ubuntu / Debian**
```bash
# Ubuntu 22.04 / 24.04 可直接使用微软源
apt-get update
apt-get install -y dotnet-sdk-10.0
```
若包源中尚未收录 10.0，先添加微软源：
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

**Arch Linux（AUR）**
```bash
yay -S dotnet-sdk-bin
```

**通用 install 脚本（所有发行版）**
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools
```

### 快速检查

```bash
dotnet --version
```

输出为 `10.x` 即可继续。

### 源码运行（推荐流程）

```bash
# 1) 进入仓库根目录
cd openclaw-dotnet

# 2) 还原依赖
dotnet restore

# 3) 启动 ProxyApi
dotnet run --project OpenClaw.ProxyApi/OpenClaw.ProxyApi.csproj

# 4) 新开终端启动 DashboardLite（可选）
dotnet run --project OpenClaw.DashboardLite/OpenClaw.DashboardLite.csproj

# 5) 或启动 McpServer
dotnet run --project OpenClaw.McpServer/OpenClaw.McpServer.csproj
```

### 本地配置文件

- `OpenClaw.ProxyApi/appsettings.Development.json`
- `OpenClaw.McpServer/appsettings.Development.json`

至少需要配置：
- `OpenClaw:GatewayUrl`
- `OpenClaw:Token`
- `OpenClaw:ApiKey`

## 运行

### 1) 配置 ProxyApi
编辑：`OpenClaw.ProxyApi/appsettings.Development.json`

```json
{
  "OpenClaw": {
    "GatewayUrl": "ws://127.0.0.1:18789",
    "Token": "<你的 gateway token>",
    "ApiKey": "<your-api-key>"
  }
}
```

- `ApiKey` 为空表示不启用对外鉴权。
- 启用后：所有请求都要带 header：`X-Api-Key: dev`

### 2) 启动 ProxyApi

```bash
cd OpenClaw.ProxyApi
DOTNET_ENVIRONMENT=Development dotnet run
```

Swagger：`http://localhost:5xxx/swagger`

### 3) 启动 DashboardLite

```bash
cd OpenClaw.DashboardLite
dotnet run
```

打开页面：
- `/` 首页
- `/gateway`：连接 SignalR、调用任意 OpenClaw method

## API

### POST /api/openclaw/call
Body:

```json
{
  "method": "sessions.list",
  "params": {}
}
```

返回：

```json
{ "ok": true, "payload": { } }
```

## Docker — McpServer

镜像名：**`gilzhang/verdure-openclaw-mcpserver:v0.1.0`**

### 构建并推送

> 构建 context 必须是仓库根目录。

```bash
# 构建
docker build -f OpenClaw.McpServer/Dockerfile \
  -t gilzhang/verdure-openclaw-mcpserver:v0.1.0 \
  -t gilzhang/verdure-openclaw-mcpserver:latest \
  .

# 推送
docker push gilzhang/verdure-openclaw-mcpserver:v0.1.0
docker push gilzhang/verdure-openclaw-mcpserver:latest
```

### 拉取并运行（服务器）

> OpenClaw Gateway 仅监听 `127.0.0.1:18789`，不对外暴露。容器桥接网络无法通过 `host.docker.internal` 访问，因此统一使用 `--network host` 模式直连宿主机 localhost。

```bash
docker run -d \
  --name verdure-openclaw-mcpserver \
  --network host \
  -e ASPNETCORE_URLS=http://0.0.0.0:3001 \
  -e OpenClaw__GatewayUrl=ws://127.0.0.1:18789 \
  -e OpenClaw__Token=你的token \
  -e OpenClaw__ApiKey=你的apikey \
  --restart unless-stopped \
  gilzhang/verdure-openclaw-mcpserver:v0.1.0
```

> `--network host` 下不要写 `-p` 端口映射；MCP 地址为 `http://<host>:3001/mcp`。  
> 此模式仅在 Linux 宿主机上生效；Windows / macOS Docker Desktop 上 host 网络有限制，建议直接在宿主机源码运行。

### MCP 端点

容器启动后，MCP Streamable HTTP 地址：`http://<host>:3001/mcp`

## 小智 AI 转接平台接入说明

参考项目：`maker-community/verdure-mcp-for-xiaozhi`

本项目可作为小智平台中的一个 MCP 服务节点使用，核心是把本服务的 `/mcp` 地址和鉴权头配置到小智平台。

### 接入参数（平台侧）

- MCP 服务地址：`http://<你的服务器IP>:3001/mcp`
- 认证方式：HTTP Header
- Header Key：`X-Api-Key`
- Header Value：与你容器环境变量 `OpenClaw__ApiKey` 一致

### 推荐流程

1. 在服务器启动容器（桥接或 host 网络二选一）。
2. 确认服务可达：`curl http://127.0.0.1:<端口>/mcp`（返回 401/405 也表示服务在线）。
3. 在小智平台新增 MCP 服务，填入上面的地址和 Header。
4. 在小智平台把该 MCP 服务绑定到目标节点。
5. 在小智对话中测试工具调用（如 sessions/list、health）。

### 排错要点

- 如果容器日志出现 `connection refused (host.docker.internal:18789)`，说明容器连不到宿主网关：
  - 在不允许修改宿主网关监听地址的前提下，请使用上面的 host 网络运行方式（`--network host` + `OpenClaw__GatewayUrl=ws://127.0.0.1:18789`）。
- 如果平台调用 MCP 返回 401，优先检查 `X-Api-Key` 是否与 `OpenClaw__ApiKey` 完全一致。
- 如果平台已连通但工具报业务错误（如 pairing/token mismatch），请检查 `OpenClaw__Token` 与网关侧 token/设备配对状态。

### 示例截图（占位）

> 以下为占位，后续请替换为真实截图文件。

![占位图：小智平台 MCP 服务配置页面](docs/images/placeholder-xiaozhi-config.png)
![占位图：容器运行与日志页面](docs/images/placeholder-container-logs.png)
![占位图：小智侧工具调用成功页面](docs/images/placeholder-tool-call-success.png)

---

## 说明
- 目前 `OpenClawDefaults.ProtocolVersion = 1`。如果你的 gateway 协议版本不是 1，需要从 OpenClaw 源码同步 `PROTOCOL_VERSION`。
- 目前只实现 token auth（connect.auth.token）。设备签名/设备 token 机制后续可加。
