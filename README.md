# OpenClaw .NET Client + Proxy API + Dashboard Lite

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

```bash
docker run -d \
  --name verdure-openclaw-mcpserver \
  -p 5223:5223 \
  -e OpenClaw__GatewayUrl=ws://host.docker.internal:18789 \
  -e OpenClaw__Token=你的token \
  -e OpenClaw__ApiKey=你的apikey \
  --add-host host.docker.internal:host-gateway \
  --restart unless-stopped \
  gilzhang/verdure-openclaw-mcpserver:v0.1.0
```

> `--add-host host.docker.internal:host-gateway` 在 Linux 服务器上必填，Docker Desktop (Windows/macOS) 不需要。  
> `OpenClaw__GatewayUrl` 填宿主机上运行的 OpenClaw gateway WebSocket 地址。

### MCP 端点

容器启动后，MCP Streamable HTTP 地址：`http://<host>:5223/mcp`

---

## 说明
- 目前 `OpenClawDefaults.ProtocolVersion = 1`。如果你的 gateway 协议版本不是 1，需要从 OpenClaw 源码同步 `PROTOCOL_VERSION`。
- 目前只实现 token auth（connect.auth.token）。设备签名/设备 token 机制后续可加。
