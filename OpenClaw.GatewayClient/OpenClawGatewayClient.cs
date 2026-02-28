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

    await SendConnectAsync(ct);
  }

  private async Task SendConnectAsync(CancellationToken ct)
  {
    if (_connectSent) return;
    _connectSent = true;

    var role = "operator";
    // 最小权限原则：不硬编码 admin。
    // 你的 token 如果本身不是 admin，也会走 scopes 校验。
    // 先声明 read/write，覆盖 sessions.list/chat.send 等常用方法。
    var scopes = new[] { "operator.read", "operator.write" };

    // Prefer stored device token if present (this is what makes CLI/TUI "just work")
    var deviceToken = _deviceTokenStore.LoadAll().TryGetValue(_deviceIdentity.DeviceId, out var entry)
      ? entry.Token
      : null;

    var authToken = deviceToken ?? _token;
    if (string.IsNullOrWhiteSpace(authToken))
      throw new InvalidOperationException("token is required (either gateway token or stored deviceToken)");

    var signedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var nonce = _connectNonce;

    // Mirror OpenClaw buildDeviceAuthPayload (v2 when nonce exists)
    var version = string.IsNullOrWhiteSpace(nonce) ? "v1" : "v2";
    var scopesCsv = string.Join(",", scopes);
    var payload = string.Join("|", new[]
    {
      version,
      _deviceIdentity.DeviceId,
      "gateway-client",
      "backend",
      role,
      scopesCsv,
      signedAtMs.ToString(),
      authToken,
      version == "v2" ? (nonce ?? "") : null
    }.Where(x => x is not null)!.Select(x => x!));

    var signature = _deviceIdentity.SignBase64Url(payload);

    var connect = new ConnectParams(
      MinProtocol: OpenClawDefaults.ProtocolVersion,
      MaxProtocol: OpenClawDefaults.ProtocolVersion,
      Client: new ClientInfo(
        Id: "gateway-client",
        Version: "0.1.0",
        Platform: System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Mode: "backend",
        InstanceId: Guid.NewGuid().ToString()
      ),
      Caps: Array.Empty<string>(),
      Auth: new AuthInfo(Token: authToken),
      Role: role,
      Scopes: scopes,
      Device: new DeviceProof(
        Id: _deviceIdentity.DeviceId,
        PublicKey: _deviceIdentity.PublicKeyRawBase64Url,
        Signature: signature,
        SignedAt: signedAtMs,
        Nonce: nonce
      )
    );

    var hello = await CallAsync<HelloOk>("connect", connect, ct);
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

    // DEBUG: log outgoing request for connect only
    if (req.Method == "connect")
    {
      try { Console.Error.WriteLine("CONNECT_REQ " + JsonSerializer.Serialize(req, _json)); } catch { }
    }

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
