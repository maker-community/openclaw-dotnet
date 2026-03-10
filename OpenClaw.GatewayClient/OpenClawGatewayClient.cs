using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OpenClaw.GatewayClient;

public sealed class OpenClawGatewayClient : IAsyncDisposable
{
  private readonly Uri _url;
  private readonly string? _token;
  private readonly ClientWebSocket _ws = new();
  private readonly JsonSerializerOptions _json;

  private readonly DeviceIdentityStore _deviceIdentityStore;
  private readonly DeviceTokenStore _deviceTokenStore;
  private readonly DeviceIdentityStore.Identity _deviceIdentity;

  private readonly CancellationTokenSource _cts = new();
  private Task? _recvLoop;

  private readonly ConcurrentDictionary<string, TaskCompletionSource<ResponseFrame>> _pending = new();

  private string? _connectNonce;
  private bool _connectSent;

  private static string MaskToken(string? token)
  {
    if (string.IsNullOrWhiteSpace(token)) return "<empty>";
    var t = token.Trim();
    if (t.Length <= 8) return "****";
    return $"{t[..4]}...{t[^4..]}";
  }

  public event Action<EventFrame>? OnEvent;

  public OpenClawGatewayClient(string? gatewayUrl = null, string? token = null)
  {
    _url = new Uri(string.IsNullOrWhiteSpace(gatewayUrl) ? OpenClawDefaults.DefaultGatewayUrl : gatewayUrl);
    _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();

    _json = new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true,
    };

    // Persist under ~/.openclaw-dotnet/
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var baseDir = Path.Combine(home, ".openclaw-dotnet");
    _deviceIdentityStore = new DeviceIdentityStore(Path.Combine(baseDir, "device-identity.json"));
    _deviceTokenStore = new DeviceTokenStore(Path.Combine(baseDir, "device-tokens.json"));
    _deviceIdentity = _deviceIdentityStore.LoadOrCreate();
  }

  public WebSocketState State => _ws.State;

  public async Task ConnectAsync(CancellationToken ct = default)
  {
    await _ws.ConnectAsync(_url, ct);
    _recvLoop = Task.Run(() => ReceiveLoop(_cts.Token));

    // wait for connect.challenge
    var start = DateTimeOffset.UtcNow;
    while (_connectNonce is null)
    {
      ct.ThrowIfCancellationRequested();
      if (DateTimeOffset.UtcNow - start > TimeSpan.FromSeconds(5))
        throw new TimeoutException("connect.challenge timeout (nonce not received)");
      await Task.Delay(50, ct);
    }

    try
    {
      await SendConnectAsync(ct);
    }
    catch
    {
      // Reset so a fresh client can retry authentication
      _connectSent = false;
      throw;
    }
  }

  private async Task SendConnectAsync(CancellationToken ct)
  {
    if (_connectSent) return;
    _connectSent = true;

    const string role = "operator";
    const string clientId = "gateway-client";
    const string mode = "backend";
    var scopes = new[] { "operator.read", "operator.write" };

    // Prefer stored device token if present (this is what makes CLI/TUI "just work")
    var deviceToken = _deviceTokenStore.LoadAll().TryGetValue(_deviceIdentity.DeviceId, out var entry)
      ? entry.Token
      : null;

    var authToken = deviceToken ?? _token;
    var authSource = string.IsNullOrWhiteSpace(deviceToken) ? "gatewayToken" : "deviceToken";
    if (string.IsNullOrWhiteSpace(authToken))
      throw new InvalidOperationException("token is required (either gateway token or stored deviceToken)");

    var signedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var nonce = _connectNonce!;
    var platform = GetNormalizedPlatform();
    var scopesCsv = string.Join(",", scopes);

    // v2 signature: v2|deviceId|clientId|mode|role|scopes|signedAt|token|nonce
    // (v2 remains accepted; v3 format is not yet publicly specified in full)
    var sigPayload = string.Join("|",
      "v2", _deviceIdentity.DeviceId, clientId, mode, role, scopesCsv,
      signedAtMs.ToString(), authToken, nonce);
    var signature = _deviceIdentity.SignBase64Url(sigPayload);

    var clientVersion = typeof(OpenClawGatewayClient).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    var connect = new ConnectParams(
      MinProtocol: OpenClawDefaults.ProtocolVersion,
      MaxProtocol: OpenClawDefaults.ProtocolVersion,
      Client: new ClientInfo(
        Id: clientId,
        Version: clientVersion,
        Platform: platform,
        Mode: mode,
        InstanceId: Guid.NewGuid().ToString()
      ),
      Caps: Array.Empty<string>(),
      Auth: new AuthInfo(Token: authToken),
      Role: role,
      Scopes: scopes,
      Commands: Array.Empty<string>(),
      Permissions: new Dictionary<string, bool>(),
      Locale: System.Globalization.CultureInfo.CurrentCulture.Name,
      UserAgent: $"openclaw-dotnet/{clientVersion}",
      Device: new DeviceProof(
        Id: _deviceIdentity.DeviceId,
        PublicKey: _deviceIdentity.PublicKeyRawBase64Url,
        Signature: signature,
        SignedAt: signedAtMs,
        Nonce: nonce
      )
    );

    HelloOk hello;
    try
    {
      hello = await CallAsync<HelloOk>("connect", connect, ct);
    }
    catch (InvalidOperationException ex)
    {
      var deviceId = _deviceIdentity.DeviceId;
      var deviceIdShort = deviceId.Length > 8 ? $"{deviceId[..8]}..." : deviceId;
      throw new InvalidOperationException(
        $"{ex.Message} (authSource={authSource}, token={MaskToken(authToken)}, deviceId={deviceIdShort})",
        ex
      );
    }

    if (!string.Equals(hello.Type, "hello-ok", StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException($"connect did not return hello-ok (got {hello.Type})");

    // Persist deviceToken if server issued one
    if (!string.IsNullOrWhiteSpace(hello.Auth?.DeviceToken))
    {
      _deviceTokenStore.Save(
        _deviceIdentity.DeviceId,
        new DeviceTokenStore.Entry(
          Role: hello.Auth.Role ?? role,
          Token: hello.Auth.DeviceToken!,
          Scopes: hello.Auth.Scopes ?? Array.Empty<string>(),
          UpdatedAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        )
      );
    }
  }

  private static string GetNormalizedPlatform()
  {
    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
      return "windows";
    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
      return "macos";
    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
      return "linux";
    return "other";
  }

  public async Task<T> CallAsync<T>(string method, object? @params = null, CancellationToken ct = default)
  {
    var payload = await CallRawAsync(method, @params, ct);
    if (!payload.HasValue)
      throw new InvalidOperationException("empty payload");
    var json = payload.Value.GetRawText();
    return JsonSerializer.Deserialize<T>(json, _json) ?? throw new InvalidOperationException("failed to deserialize payload");
  }

  public async Task<JsonElement?> CallRawAsync(string method, object? @params = null, CancellationToken ct = default)
  {
    if (_ws.State != WebSocketState.Open)
      throw new InvalidOperationException("gateway not connected");

    var id = Guid.NewGuid().ToString();
    var req = new RequestFrame("req", id, method, @params);

    var tcs = new TaskCompletionSource<ResponseFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
    _pending[id] = tcs;

    await SendJsonAsync(req, ct);

    using var reg = ct.Register(() =>
    {
      if (_pending.TryRemove(id, out var pending))
        pending.TrySetCanceled(ct);
    });

    var res = await tcs.Task;
    if (!res.Ok)
      throw new InvalidOperationException(res.Error?.Message ?? "gateway error");

    return res.Payload;
  }

  private async Task SendJsonAsync<T>(T obj, CancellationToken ct)
  {
    var json = JsonSerializer.Serialize(obj, _json);
    var bytes = Encoding.UTF8.GetBytes(json);
    await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
  }

  private async Task ReceiveLoop(CancellationToken ct)
  {
    var buffer = new byte[1024 * 1024];
    var sb = new StringBuilder();

    while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
    {
      sb.Clear();
      WebSocketReceiveResult r;
      do
      {
        r = await _ws.ReceiveAsync(buffer, ct);
        if (r.MessageType == WebSocketMessageType.Close)
          return;
        sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
      } while (!r.EndOfMessage);

      HandleIncoming(sb.ToString());
    }
  }

  private void HandleIncoming(string raw)
  {
    try
    {
      using var doc = JsonDocument.Parse(raw);
      var root = doc.RootElement;
      if (!root.TryGetProperty("type", out var typeEl))
        return;
      var type = typeEl.GetString();

      if (type == "event")
      {
        var evt = JsonSerializer.Deserialize<EventFrame>(raw, _json);
        if (evt is null) return;

        if (evt.Event == "connect.challenge")
        {
          if (evt.Payload is { } payload && payload.ValueKind == JsonValueKind.Object &&
              payload.TryGetProperty("nonce", out var nonceEl))
          {
            var nonce = nonceEl.GetString();
            if (!string.IsNullOrWhiteSpace(nonce))
              _connectNonce = nonce.Trim();
          }
        }

        OnEvent?.Invoke(evt);
        return;
      }

      if (type == "res")
      {
        var res = JsonSerializer.Deserialize<ResponseFrame>(raw, _json);
        if (res is null) return;

        if (_pending.TryRemove(res.Id, out var tcs))
          tcs.TrySetResult(res);
        return;
      }
    }
    catch
    {
      // ignore parse errors
    }
  }

  // ─── Health & Status ───────────────────────────────────────────────────────

  /// <summary>Returns gateway health status.</summary>
  public Task<HealthResult> HealthAsync(CancellationToken ct = default)
    => CallAsync<HealthResult>("health", null, ct);

  /// <summary>Returns raw gateway status payload.</summary>
  public Task<JsonElement?> StatusAsync(CancellationToken ct = default)
    => CallRawAsync("status", null, ct);

  // ─── System Presence ───────────────────────────────────────────────────────

  /// <summary>Returns all currently connected clients (deviceId, role, scopes, platform).</summary>
  public Task<SystemPresenceResult> SystemPresenceAsync(CancellationToken ct = default)
    => CallAsync<SystemPresenceResult>("system-presence", null, ct);

  // ─── Sessions ──────────────────────────────────────────────────────────────

  /// <summary>Lists all active sessions.</summary>
  public Task<SessionsListResult> SessionsListAsync(int? limit = null, CancellationToken ct = default)
    => CallAsync<SessionsListResult>("sessions.list",
        limit.HasValue ? (object)new { limit = limit.Value } : new { }, ct);

  /// <summary>Gets a single session by <paramref name="sessionKey"/>.</summary>
  public Task<SessionInfo> SessionsGetAsync(string sessionKey, CancellationToken ct = default)
    => CallAsync<SessionInfo>("sessions.get", new { sessionKey }, ct);

  /// <summary>
  /// Patches a session's settings. Only non-null fields in <paramref name="patch"/> are sent.
  /// </summary>
  public Task<SessionInfo> SessionsPatchAsync(string sessionKey, SessionPatch patch, CancellationToken ct = default)
  {
    var d = new Dictionary<string, object?> { ["sessionKey"] = sessionKey };
    if (patch.Model is not null) d["model"] = patch.Model;
    if (patch.ThinkingLevel is not null) d["thinkingLevel"] = patch.ThinkingLevel;
    if (patch.VerboseLevel is not null) d["verboseLevel"] = patch.VerboseLevel;
    if (patch.SendPolicy is not null) d["sendPolicy"] = patch.SendPolicy;
    if (patch.GroupActivation is not null) d["groupActivation"] = patch.GroupActivation;
    if (patch.Elevated.HasValue) d["elevated"] = patch.Elevated.Value;
    return CallAsync<SessionInfo>("sessions.patch", d, ct);
  }

  /// <summary>Returns message history for a session.</summary>
  public Task<SessionHistoryResult> SessionsHistoryAsync(
    string sessionKey, int? limit = null, string? before = null, CancellationToken ct = default)
  {
    var d = new Dictionary<string, object?> { ["sessionKey"] = sessionKey };
    if (limit.HasValue) d["limit"] = limit.Value;
    if (before is not null) d["before"] = before;
    return CallAsync<SessionHistoryResult>("sessions.history", d, ct);
  }

  /// <summary>Spawns a new session (sub-agent / group isolation).</summary>
  public Task<SessionInfo> SessionsSpawnAsync(
    string sessionKey, string? description = null, CancellationToken ct = default)
  {
    var d = new Dictionary<string, object?> { ["sessionKey"] = sessionKey };
    if (description is not null) d["description"] = description;
    return CallAsync<SessionInfo>("sessions.spawn", d, ct);
  }

  // ─── Chat ──────────────────────────────────────────────────────────────────

  /// <summary>
  /// Sends a chat message to a session.
  /// An idempotency key is auto-generated when not supplied.
  /// </summary>
  public Task<ChatSendResult> ChatSendAsync(
    string sessionKey, string message,
    string? idempotencyKey = null, CancellationToken ct = default)
    => CallAsync<ChatSendResult>("chat.send", new
    {
      sessionKey,
      message,
      idempotencyKey = idempotencyKey ?? Guid.NewGuid().ToString()
    }, ct);

  /// <summary>
  /// Returns chat history for a session using the chat-native endpoint.
  /// This is typically less restrictive than sessions.history.
  /// </summary>
  public Task<SessionHistoryResult> ChatHistoryAsync(
    string sessionKey, int? limit = null, CancellationToken ct = default)
  {
    var d = new Dictionary<string, object?> { ["sessionKey"] = sessionKey };
    if (limit.HasValue) d["limit"] = limit.Value;
    return CallAsync<SessionHistoryResult>("chat.history", d, ct);
  }

  // ─── Agent ─────────────────────────────────────────────────────────────────

  /// <summary>
  /// Runs the agent with a message.  An idempotency key is auto-generated when not supplied.
  /// </summary>
  public Task<AgentResult> AgentAsync(
    string sessionKey, string message,
    string? idempotencyKey = null, string? thinkingLevel = null,
    CancellationToken ct = default)
  {
    var d = new Dictionary<string, object?>
    {
      ["sessionKey"] = sessionKey,
      ["message"] = message,
      ["idempotencyKey"] = idempotencyKey ?? Guid.NewGuid().ToString()
    };
    if (thinkingLevel is not null) d["thinkingLevel"] = thinkingLevel;
    return CallAsync<AgentResult>("agent", d, ct);
  }

  // ─── Tools ─────────────────────────────────────────────────────────────────

  /// <summary>Returns the runtime tool catalog for the given session (or the default agent).</summary>
  public Task<ToolsCatalogResult> ToolsCatalogAsync(string? sessionKey = null, CancellationToken ct = default)
    => CallAsync<ToolsCatalogResult>("tools.catalog",
        sessionKey is not null ? (object)new { sessionKey } : new { }, ct);

  /// <summary>Invokes a gateway tool by name.</summary>
  public Task<JsonElement?> ToolsInvokeAsync(
    string tool, object? input = null, string? sessionKey = null, CancellationToken ct = default)
  {
    var d = new Dictionary<string, object?> { ["tool"] = tool };
    if (input is not null) d["input"] = input;
    if (sessionKey is not null) d["sessionKey"] = sessionKey;
    return CallRawAsync("tools.invoke", d, ct);
  }

  // ─── Nodes ─────────────────────────────────────────────────────────────────

  /// <summary>Lists all connected nodes (macOS, iOS, Android, headless).</summary>
  public Task<NodeListResult> NodeListAsync(CancellationToken ct = default)
    => CallAsync<NodeListResult>("node.list", null, ct);

  /// <summary>Returns capabilities and permissions for a specific node.</summary>
  public Task<NodeInfo> NodeDescribeAsync(string nodeId, CancellationToken ct = default)
    => CallAsync<NodeInfo>("node.describe", new { nodeId }, ct);

  /// <summary>
  /// Invokes a command on a node (e.g. <c>camera.snap</c>, <c>system.run</c>, <c>location.get</c>).
  /// </summary>
  public Task<JsonElement?> NodeInvokeAsync(
    string nodeId, string command, object? args = null, CancellationToken ct = default)
  {
    var d = new Dictionary<string, object?> { ["nodeId"] = nodeId, ["command"] = command };
    if (args is not null) d["args"] = args;
    return CallRawAsync("node.invoke", d, ct);
  }

  // ─── Exec approvals ────────────────────────────────────────────────────────

  /// <summary>
  /// Resolves a pending exec approval request.
  /// Requires <c>operator.approvals</c> scope.
  /// </summary>
  public Task<JsonElement?> ExecApprovalResolveAsync(
    string approvalId, bool approved, CancellationToken ct = default)
    => CallRawAsync("exec.approval.resolve", new { approvalId, approved }, ct);

  // ─── Device tokens ─────────────────────────────────────────────────────────

  /// <summary>Rotates the device token for a given device. Requires <c>operator.pairing</c> scope.</summary>
  public Task<JsonElement?> DeviceTokenRotateAsync(string deviceId, CancellationToken ct = default)
    => CallRawAsync("device.token.rotate", new { deviceId }, ct);

  /// <summary>Revokes the device token for a given device. Requires <c>operator.pairing</c> scope.</summary>
  public Task<JsonElement?> DeviceTokenRevokeAsync(string deviceId, CancellationToken ct = default)
    => CallRawAsync("device.token.revoke", new { deviceId }, ct);

  public async ValueTask DisposeAsync()
  {
    _cts.Cancel();
    try
    {
      if (_ws.State == WebSocketState.Open)
        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
    }
    catch { }

    _ws.Dispose();
    _cts.Dispose();
  }
}
