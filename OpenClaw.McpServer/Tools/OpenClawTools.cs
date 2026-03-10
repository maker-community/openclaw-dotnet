using ModelContextProtocol.Server;
using OpenClaw.GatewayClient;
using System.ComponentModel;
using System.Text.Json;
using System.Linq;

namespace OpenClaw.McpServer.Tools;

[McpServerToolType]
public sealed class OpenClawTools
{
  private readonly OpenClawGatewayService _gw;

  public OpenClawTools(OpenClawGatewayService gw) => _gw = gw;

  public sealed record CallResult(bool ok, JsonElement? payload);
  public sealed record ChatSessionLite(string sessionKey, string? label, string? model, string? status);
  public sealed record ChatSessionsResult(bool ok, ChatSessionLite[] sessions);
  public sealed record ChatMessageLite(string role, string content, long? ts, string? id);
  public sealed record ChatHistoryLiteResult(bool ok, string sessionKey, ChatMessageLite[] messages, string source);
  public sealed record ChatSyncResult(
    bool ok,
    string sessionKey,
    string? runId,
    string? status,
    string? assistantReply,
    ChatMessageLite[] historyTail,
    string source);

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
    return await GetHistoryPreferChatAsync(sessionKey, limit, ct);
  }

  [McpServerTool, Description("List chat sessions in a simplified format with normalized sessionKey. Supports optional keyword filter.")]
  public async Task<ChatSessionsResult> OpenClawChatSessions(
    [Description("Optional max number of sessions to return.")] int? limit,
    [Description("Optional keyword match on key/label/model.")] string? search,
    CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    var payload = await _gw.Client.SessionsListAsync(limit, ct);

    var items = (payload.Sessions ?? Array.Empty<SessionInfo>())
      .Select(s => new ChatSessionLite(
        sessionKey: ResolveSessionKey(s),
        label: FirstNonEmpty(s.Label, s.Title),
        model: s.Model,
        status: s.Status))
      .Where(x => !string.IsNullOrWhiteSpace(x.sessionKey));

    if (!string.IsNullOrWhiteSpace(search))
    {
      var q = search.Trim();
      items = items.Where(x =>
        ContainsIgnoreCase(x.sessionKey, q) ||
        ContainsIgnoreCase(x.label, q) ||
        ContainsIgnoreCase(x.model, q));
    }

    return new ChatSessionsResult(true, items.ToArray());
  }

  [McpServerTool, Description("Get simplified chat history (role/content/ts), automatically compatible with scope differences.")]
  public async Task<ChatHistoryLiteResult> OpenClawChatHistoryLite(
    [Description("Session key, e.g. main or agent:main:main")] string sessionKey,
    [Description("Max number of messages to return.")] int? limit,
    CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    var (history, source) = await GetHistoryWithSourceAsync(sessionKey, limit, ct);
    return new ChatHistoryLiteResult(true, sessionKey, ToLiteMessages(history), source);
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

  [McpServerTool, Description("Send chat and synchronously wait for an assistant reply from history. Returns reply text and recent tail.")]
  public async Task<ChatSyncResult> OpenClawChatSendAndWait(
    [Description("Session key, e.g. main or agent:main:main")] string sessionKey,
    [Description("Message text to send.")] string message,
    [Description("Max wait time in milliseconds (1000-20000). Default 6000.")] int? waitMs,
    [Description("Polling interval in milliseconds (200-2000). Default 500.")] int? pollIntervalMs,
    CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);

    var sendTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var send = await _gw.Client.ChatSendAsync(sessionKey, message, Guid.NewGuid().ToString(), ct);

    var maxWait = Math.Clamp(waitMs ?? 6000, 1000, 20000);
    var interval = Math.Clamp(pollIntervalMs ?? 500, 200, 2000);
    var deadline = DateTimeOffset.UtcNow.AddMilliseconds(maxWait);

    SessionHistoryResult? lastHistory = null;
    string source = "chat.history";

    while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
    {
      var result = await GetHistoryWithSourceAsync(sessionKey, 50, ct);
      lastHistory = result.history;
      source = result.source;

      var assistant = FindAssistantReply(lastHistory, sendTs);
      if (!string.IsNullOrWhiteSpace(assistant))
      {
        return new ChatSyncResult(
          ok: true,
          sessionKey: sessionKey,
          runId: send.RunId,
          status: send.Status,
          assistantReply: assistant,
          historyTail: ToLiteMessages(lastHistory).TakeLast(10).ToArray(),
          source: source);
      }

      await Task.Delay(interval, ct);
    }

    var fallbackTail = lastHistory is null ? Array.Empty<ChatMessageLite>() : ToLiteMessages(lastHistory).TakeLast(10).ToArray();
    return new ChatSyncResult(
      ok: true,
      sessionKey: sessionKey,
      runId: send.RunId,
      status: send.Status,
      assistantReply: null,
      historyTail: fallbackTail,
      source: source);
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

  private async Task<SessionHistoryResult> GetHistoryPreferChatAsync(string sessionKey, int? limit, CancellationToken ct)
  {
    var (history, _) = await GetHistoryWithSourceAsync(sessionKey, limit, ct);
    return history;
  }

  private async Task<(SessionHistoryResult history, string source)> GetHistoryWithSourceAsync(string sessionKey, int? limit, CancellationToken ct)
  {
    try
    {
      var history = await _gw.Client.ChatHistoryAsync(sessionKey, limit, ct);
      return (history, "chat.history");
    }
    catch (InvalidOperationException ex) when (HasMissingScope(ex))
    {
      var history = await _gw.Client.SessionsHistoryAsync(sessionKey, limit, null, ct);
      return (history, "sessions.history");
    }
  }

  private static ChatMessageLite[] ToLiteMessages(SessionHistoryResult history)
    => (history.Messages ?? Array.Empty<MessageEntry>())
      .Select(m => new ChatMessageLite(
        role: string.IsNullOrWhiteSpace(m.Role) ? "assistant" : m.Role,
        content: NormalizeContent(m.Content),
        ts: m.Ts,
        id: m.Id))
      .ToArray();

  private static string? FindAssistantReply(SessionHistoryResult history, long sendTs)
  {
    var messages = history.Messages ?? Array.Empty<MessageEntry>();
    return messages
      .Where(m =>
        string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
        (!m.Ts.HasValue || m.Ts.Value >= sendTs))
      .Select(m => NormalizeContent(m.Content))
      .LastOrDefault(x => !string.IsNullOrWhiteSpace(x) && x != "[empty]");
  }

  private static string NormalizeContent(JsonElement? content)
  {
    if (!content.HasValue || content.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
      return "[empty]";

    var v = content.Value;
    if (v.ValueKind == JsonValueKind.String)
      return string.IsNullOrWhiteSpace(v.GetString()) ? "[empty]" : v.GetString()!;

    if (v.ValueKind == JsonValueKind.Array)
    {
      var parts = v.EnumerateArray().Select(RenderArrayItem).Where(s => !string.IsNullOrWhiteSpace(s));
      var joined = string.Join("\n", parts);
      return string.IsNullOrWhiteSpace(joined) ? "[empty]" : joined;
    }

    return v.ToString();
  }

  private static string RenderArrayItem(JsonElement item)
  {
    if (item.ValueKind == JsonValueKind.String)
      return item.GetString() ?? string.Empty;

    if (item.ValueKind == JsonValueKind.Object)
    {
      if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        return text.GetString() ?? string.Empty;

      if (item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
          && item.TryGetProperty("content", out var content))
        return $"[{type.GetString()}] {content}";
    }

    return item.ToString();
  }

  private static bool HasMissingScope(InvalidOperationException ex)
    => ex.Message.Contains("missing scope:", StringComparison.OrdinalIgnoreCase);

  private static string ResolveSessionKey(SessionInfo s)
    => FirstNonEmpty(s.SessionKey, s.Key, s.SessionId) ?? (s.IsMain == true ? "main" : string.Empty);

  private static string? FirstNonEmpty(params string?[] values)
    => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

  private static bool ContainsIgnoreCase(string? source, string query)
    => !string.IsNullOrWhiteSpace(source) && source.Contains(query, StringComparison.OrdinalIgnoreCase);
}
