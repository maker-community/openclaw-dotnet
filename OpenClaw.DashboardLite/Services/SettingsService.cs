using Microsoft.JSInterop;

namespace OpenClaw.DashboardLite.Services;

public sealed class SettingsService
{
  private const string StorageKey = "openclaw.dashboard.settings.v1";

  private readonly IJSRuntime _js;

  public SettingsService(IJSRuntime js) => _js = js;

  public sealed record Settings(string ProxyApiBaseUrl, string? ApiKey);

  public async Task<Settings> GetAsync()
  {
    try
    {
      var json = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
      if (!string.IsNullOrWhiteSpace(json))
      {
        var s = System.Text.Json.JsonSerializer.Deserialize<Settings>(json);
        if (s is not null)
        {
          if (string.Equals(s.ProxyApiBaseUrl, "http://127.0.0.1:5010", StringComparison.OrdinalIgnoreCase))
            return new Settings(string.Empty, s.ApiKey);

          return s;
        }
      }
    }
    catch { }

    return new Settings(string.Empty, null);
  }

  public async Task SaveAsync(Settings settings)
  {
    var normalized = settings.ProxyApiBaseUrl.Trim().TrimEnd('/');
    var s = settings with { ProxyApiBaseUrl = normalized };
    var json = System.Text.Json.JsonSerializer.Serialize(s);
    await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
  }
}
