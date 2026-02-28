using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenClaw.GatewayClient;

public sealed class DeviceTokenStore
{
  private readonly string _path;

  public DeviceTokenStore(string path)
  {
    _path = path;
  }

  public sealed record Entry(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("scopes")] string[] Scopes,
    [property: JsonPropertyName("updatedAtMs")] long UpdatedAtMs
  );

  public Dictionary<string, Entry> LoadAll()
  {
    try
    {
      if (!File.Exists(_path)) return new();
      var json = File.ReadAllText(_path);
      return JsonSerializer.Deserialize<Dictionary<string, Entry>>(json) ?? new();
    }
    catch { return new(); }
  }

  public void Save(string deviceId, Entry entry)
  {
    var all = LoadAll();
    all[deviceId] = entry;

    var dir = Path.GetDirectoryName(_path);
    if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
    var json = JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(_path, json + "\n");

    try { File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
  }
}
