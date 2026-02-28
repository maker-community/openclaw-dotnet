namespace OpenClaw.GatewayClient;

internal static class Base64Url
{
  public static string Encode(byte[] bytes)
    => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

  public static byte[] Decode(string s)
  {
    var b64 = s.Replace('-', '+').Replace('_', '/');
    switch (b64.Length % 4)
    {
      case 2: b64 += "=="; break;
      case 3: b64 += "="; break;
    }
    return Convert.FromBase64String(b64);
  }
}
