namespace OpenClaw.DashboardLite;

public sealed class AppConfig
{
  public string ProxyApiBaseUrl { get; set; } = "http://127.0.0.1:5010";
  public string? ApiKey { get; set; }
}
