using OpenClaw.GatewayClient;

namespace OpenClaw.ProxyApi;

public sealed class OpenClawGatewayService : IAsyncDisposable
{
  private readonly ILogger<OpenClawGatewayService> _log;
  private readonly OpenClawGatewayClient _client;

  public OpenClawGatewayClient Client => _client;

  public OpenClawGatewayService(ILogger<OpenClawGatewayService> log, OpenClawOptions opts)
  {
    _log = log;
    _client = new OpenClawGatewayClient(opts.GatewayUrl, opts.Token);
  }

  public async Task EnsureConnectedAsync(CancellationToken ct)
  {
    if (_client.State == System.Net.WebSockets.WebSocketState.Open) return;

    _log.LogInformation("Connecting to OpenClaw gateway...");
    await _client.ConnectAsync(ct);
    _log.LogInformation("Connected.");
  }

  public ValueTask DisposeAsync() => _client.DisposeAsync();
}
