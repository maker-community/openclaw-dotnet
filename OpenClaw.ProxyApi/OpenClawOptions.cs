namespace OpenClaw.ProxyApi;

public sealed class OpenClawOptions
{
  public string GatewayUrl { get; set; } = "ws://127.0.0.1:18789";
  public string? Token { get; set; }
  // 对外 API 鉴权用（简单起见先用一个 shared key）
  public string? ApiKey { get; set; }
}
