using Microsoft.Extensions.Options;
using OpenClaw.GatewayClient;

namespace OpenClaw.McpServer;

public sealed class OpenClawGatewayService : IAsyncDisposable
{
  private readonly ILogger<OpenClawGatewayService> _log;
  private readonly OpenClawGatewayClient _client;
  private readonly SemaphoreSlim _connectLock = new(1, 1);

  public OpenClawGatewayClient Client => _client;

  public OpenClawGatewayService(ILogger<OpenClawGatewayService> log, IOptions<McpOpenClawOptions> opts)
  {
    _log = log;
    var o = opts.Value;
    _client = new OpenClawGatewayClient(o.GatewayUrl, o.Token);
  }

  public async Task EnsureConnectedAsync(CancellationToken ct)
  {
    var state = _client.State;
    if (state == System.Net.WebSockets.WebSocketState.Open) return;
    if (state == System.Net.WebSockets.WebSocketState.Connecting) return;

    await _connectLock.WaitAsync(ct);
    try
    {
      // Re-check after acquiring the lock
      state = _client.State;
      if (state == System.Net.WebSockets.WebSocketState.Open) return;
      if (state == System.Net.WebSockets.WebSocketState.Connecting) return;

      _log.LogInformation("Connecting to OpenClaw gateway...");
      await _client.ConnectAsync(ct);
      _log.LogInformation("Connected.");
    }
    finally
    {
      _connectLock.Release();
    }
  }

  public async ValueTask DisposeAsync()
  {
    _connectLock.Dispose();
    await _client.DisposeAsync();
  }
}
