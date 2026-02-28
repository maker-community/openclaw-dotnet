namespace OpenClaw.McpServer;

public sealed class McpOpenClawOptions
{
  public string GatewayUrl { get; set; } = "ws://127.0.0.1:18789";
  public string? Token { get; set; }
  public string? ApiKey { get; set; } = "dev";
}
