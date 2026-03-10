using ModelContextProtocol.Server;
using OpenClaw.GatewayClient;
using System.ComponentModel;
using System.Text.Json;

namespace OpenClaw.McpServer.Tools;

[McpServerToolType]
public sealed class OpenClawTools
{
  private readonly OpenClawGatewayService _gw;

  public OpenClawTools(OpenClawGatewayService gw) => _gw = gw;

  public sealed record CallResult(bool ok, JsonElement? payload);

  // ─── Generic pass-through ─────────────────────────────────────────────────

  [McpServerTool, Description("Call any OpenClaw gateway method (full access). Params must be a JSON object.")]
  public async Task<CallResult> OpenClawCall(
    [Description("Gateway method name, e.g. sessions.list, chat.send, tools.invoke")] string method,
    [Description("Params JSON object. Example: {\"limit\":10}")] JsonElement @params,
    CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    var payload = await _gw.Client.CallRawAsync(method, @params, ct);
    return new CallResult(true, payload);
  }

  // ─── Health ───────────────────────────────────────────────────────────────

  [McpServerTool, Description("Check OpenClaw gateway health. Returns ok, status, gatewayVersion and uptimeMs.")]
  public async Task<HealthResult> OpenClawHealth(CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    return await _gw.Client.HealthAsync(ct);
  }

  // ─── System presence ─────────────────────────────────────────────────────

  [McpServerTool, Description("List all connected clients (macOS app, CLI, iOS/Android nodes) with their deviceId, role, scopes and platform.")]
  public async Task<SystemPresenceResult> OpenClawSystemPresence(CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    return await _gw.Client.SystemPresenceAsync(ct);
  }

  // ─── Sessions ─────────────────────────────────────────────────────────────

  [McpServerTool, Description("List all active OpenClaw sessions. Returns sessionKey, label, model, status, role, thinkingLevel.")]
  public async Task<SessionsListResult> OpenClawSessionsList(
    [Description("Optional max number of sessions to return.")] int? limit,
    CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    return await _gw.Client.SessionsListAsync(limit, ct);
  }

  [McpServerTool, Description("Get a single OpenClaw session by sessionKey (e.g. 'agent:main:main').")]
  public async Task<SessionInfo> OpenClawSessionsGet(
    [Description("Session key, e.g. agent:main:main")] string sessionKey,
    CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    return await _gw.Client.SessionsGetAsync(sessionKey, ct);
  }

  [McpServerTool, Description("Get message history for an OpenClaw session.")]
  public async Task<SessionHistoryResult> OpenClawSessionsHistory(
    [Description("Session key, e.g. agent:main:main")] string sessionKey,
    [Description("Max number of messages to return.")] int? limit,
    CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    return await _gw.Client.SessionsHistoryAsync(sessionKey, limit, null, ct);
  }

  // ─── Chat ─────────────────────────────────────────────────────────────────

  [McpServerTool, Description("Send a chat message via OpenClaw. Returns runId and status.")]
  public async Task<ChatSendResult> OpenClawChatSend(
    [Description("OpenClaw sessionKey, e.g. agent:main:main")] string sessionKey,
    [Description("Message text to send.")] string message,
    [Description("Optional idempotency key for de-duplication. Auto-generated if omitted.")] string? idempotencyKey,
    CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    return await _gw.Client.ChatSendAsync(sessionKey, message, idempotencyKey, ct);
  }

  // ─── Agent ────────────────────────────────────────────────────────────────

  [McpServerTool, Description("Run the OpenClaw agent with a message. Returns runId and status.")]
  public async Task<AgentResult> OpenClawAgent(
    [Description("OpenClaw sessionKey, e.g. agent:main:main")] string sessionKey,
    [Description("Message to pass to the agent.")] string message,
    [Description("Optional thinking level: off, minimal, low, medium, high, xhigh.")] string? thinkingLevel,
    CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    return await _gw.Client.AgentAsync(sessionKey, message, null, thinkingLevel, ct);
  }

  // ─── Tools catalog ────────────────────────────────────────────────────────

  [McpServerTool, Description("Get the runtime tool catalog available to an OpenClaw session.")]
  public async Task<ToolsCatalogResult> OpenClawToolsCatalog(
    [Description("Optional sessionKey to scope the catalog lookup.")] string? sessionKey,
    CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    return await _gw.Client.ToolsCatalogAsync(sessionKey, ct);
  }

  // ─── Nodes ────────────────────────────────────────────────────────────────

  [McpServerTool, Description("List all connected OpenClaw nodes (macOS, iOS, Android, headless) with their capabilities and commands.")]
  public async Task<NodeListResult> OpenClawNodeList(CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    return await _gw.Client.NodeListAsync(ct);
  }

  [McpServerTool, Description("Invoke a command on a connected OpenClaw node, e.g. camera.snap, system.run, location.get.")]
  public async Task<CallResult> OpenClawNodeInvoke(
    [Description("Device ID of the target node.")] string nodeId,
    [Description("Command name, e.g. camera.snap, system.run, location.get.")] string command,
    [Description("Optional command arguments as a JSON object.")] JsonElement? args,
    CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    object? argsObj = args is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } a ? a : null;
    var payload = await _gw.Client.NodeInvokeAsync(nodeId, command, argsObj, ct);
    return new CallResult(true, payload);
  }

  // ─── MCP server health ────────────────────────────────────────────────────

  [McpServerTool, Description("Health check for the MCP server (does not call OpenClaw).")]
  public object Health() => new { ok = true };
}
