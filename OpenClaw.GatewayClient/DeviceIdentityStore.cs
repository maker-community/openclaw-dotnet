using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenClaw.GatewayClient;

public sealed class DeviceIdentityStore
{
  private readonly string _path;

  public DeviceIdentityStore(string path)
  {
    _path = path;
  }

  public sealed record Stored(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("publicKeyRawBase64Url")] string PublicKeyRawBase64Url,
    [property: JsonPropertyName("privateKeyRawBase64Url")] string PrivateKeyRawBase64Url,
    [property: JsonPropertyName("createdAtMs")] long CreatedAtMs
  );

  public sealed record Identity(string DeviceId, byte[] PublicKeyRaw, byte[] PrivateKeyRaw)
  {
    public string PublicKeyRawBase64Url => Base64Url.Encode(PublicKeyRaw);

    public string SignBase64Url(string payload)
    {
      var msg = Encoding.UTF8.GetBytes(payload);
      var sig = new byte[LibSodium.crypto_sign_BYTES];
      var rc = LibSodium.crypto_sign_detached(sig, out var sigLen, msg, (ulong)msg.Length, PrivateKeyRaw);
      if (rc != 0) throw new InvalidOperationException($"crypto_sign_detached failed rc={rc}");
      if (sigLen != (ulong)sig.Length) Array.Resize(ref sig, (int)sigLen);
      return Base64Url.Encode(sig);
    }

    public static string ComputeDeviceId(byte[] publicKeyRaw)
    {
      var hash = SHA256.HashData(publicKeyRaw);
      return Convert.ToHexString(hash).ToLowerInvariant();
    }
  }

  public Identity LoadOrCreate()
  {
    LibSodium.sodium_init();

    try
    {
      if (File.Exists(_path))
      {
        var json = File.ReadAllText(_path);
        var stored = JsonSerializer.Deserialize<Stored>(json);
        if (stored is not null && stored.Version == 1)
        {
          var pub = Base64Url.Decode(stored.PublicKeyRawBase64Url);
          var priv = Base64Url.Decode(stored.PrivateKeyRawBase64Url);
          var derivedId = Identity.ComputeDeviceId(pub);
          if (!string.Equals(derivedId, stored.DeviceId, StringComparison.OrdinalIgnoreCase))
          {
            Persist(stored with { DeviceId = derivedId });
          }
          return new Identity(derivedId, pub, priv);
        }
      }
    }
    catch { }

    var pk = new byte[LibSodium.crypto_sign_PUBLICKEYBYTES];
    var sk = new byte[LibSodium.crypto_sign_SECRETKEYBYTES];
    var rc2 = LibSodium.crypto_sign_keypair(pk, sk);
    if (rc2 != 0) throw new InvalidOperationException($"crypto_sign_keypair failed rc={rc2}");

    var deviceId = Identity.ComputeDeviceId(pk);
    var created = new Stored(
      Version: 1,
      DeviceId: deviceId,
      PublicKeyRawBase64Url: Base64Url.Encode(pk),
      PrivateKeyRawBase64Url: Base64Url.Encode(sk),
      CreatedAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    );

    Persist(created);
    return new Identity(deviceId, pk, sk);
  }

  private void Persist(Stored stored)
  {
    var dir = Path.GetDirectoryName(_path);
    if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

    var json = JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(_path, json + "\n");

    try { File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
  }
}
