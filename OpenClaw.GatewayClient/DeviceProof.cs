using System.Text.Json.Serialization;

namespace OpenClaw.GatewayClient;

public sealed record DeviceProof(
  [property: JsonPropertyName("id")] string Id,
  [property: JsonPropertyName("publicKey")] string PublicKey,
  [property: JsonPropertyName("signature")] string Signature,
  [property: JsonPropertyName("signedAt")] long SignedAt,
  [property: JsonPropertyName("nonce")] string? Nonce = null
);
