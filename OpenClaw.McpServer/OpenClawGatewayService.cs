using Microsoft.Extensions.Options;
using OpenClaw.GatewayClient;

namespace OpenClaw.McpServer;

public sealed class OpenClawGatewayService : IAsyncDisposable
{
  private readonly ILogger<OpenClawGatewayService> _log;
  private readonly OpenClawGatewayClient _client;

  public OpenClawGatewayClient Client => _client;

  public OpenClawGatewayService(ILogger<OpenClawGatewayService> log, IOptions<McpOpenClawOptions> opts)
  {
    _log = log;
    var o = opts.Value;
    _client = new OpenClawGatewayClient(o.GatewayUrl, o.Token);
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
