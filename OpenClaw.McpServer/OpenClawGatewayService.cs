using Microsoft.Extensions.Options;
using OpenClaw.GatewayClient;

namespace OpenClaw.McpServer;

public sealed class OpenClawGatewayService : IAsyncDisposable
{
  private readonly ILogger<OpenClawGatewayService> _log;
  private readonly McpOpenClawOptions _opts;
  private readonly SemaphoreSlim _connectLock = new(1, 1);

  private OpenClawGatewayClient _client;
  private bool _authenticated;

  public OpenClawGatewayClient Client => _client;

  public OpenClawGatewayService(ILogger<OpenClawGatewayService> log, IOptions<McpOpenClawOptions> opts)
  {
    _log = log;
    _opts = opts.Value;
    _client = new OpenClawGatewayClient(_opts.GatewayUrl, _opts.Token);
  }

  public async Task EnsureConnectedAsync(CancellationToken ct)
  {
    if (_authenticated && _client.State == System.Net.WebSockets.WebSocketState.Open) return;

    await _connectLock.WaitAsync(ct);
    try
    {
      // Re-check after acquiring the lock
      if (_authenticated && _client.State == System.Net.WebSockets.WebSocketState.Open) return;

      // Recreate client if the previous instance is no longer usable
      if (_client.State != System.Net.WebSockets.WebSocketState.None)
      {
        await _client.DisposeAsync();
        _client = new OpenClawGatewayClient(_opts.GatewayUrl, _opts.Token);
        _authenticated = false;
      }

      _log.LogInformation("Connecting to OpenClaw gateway...");
      await _client.ConnectAsync(ct);
      _authenticated = true;
      _log.LogInformation("Connected.");
    }
    catch
    {
      _authenticated = false;
      throw;
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
