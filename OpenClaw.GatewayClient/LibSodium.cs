using System.Runtime.InteropServices;

namespace OpenClaw.GatewayClient;

internal static class LibSodium
{
  // Ubuntu provides libsodium.so.23 (soname). Use the explicit soname to avoid resolution issues.
  private const string Lib = "libsodium.so.23";

  [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
  internal static extern int sodium_init();

  [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
  internal static extern int crypto_sign_keypair(byte[] pk, byte[] sk);

  [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
  internal static extern int crypto_sign_detached(byte[] sig, out ulong siglen, byte[] m, ulong mlen, byte[] sk);

  [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
  internal static extern int crypto_sign_verify_detached(byte[] sig, byte[] m, ulong mlen, byte[] pk);

  internal const int crypto_sign_PUBLICKEYBYTES = 32;
  internal const int crypto_sign_SECRETKEYBYTES = 64;
  internal const int crypto_sign_BYTES = 64;
}
