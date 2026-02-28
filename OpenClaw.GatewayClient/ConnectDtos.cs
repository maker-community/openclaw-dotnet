using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenClaw.GatewayClient;

public static class OpenClawDefaults
{
  public const int ProtocolVersion = 3;
  public const string DefaultGatewayUrl = "ws://127.0.0.1:18789";
}

public sealed record ConnectParams(
  [property: JsonPropertyName("minProtocol")] int MinProtocol,
  [property: JsonPropertyName("maxProtocol")] int MaxProtocol,
  [property: JsonPropertyName("client")] ClientInfo Client,
  [property: JsonPropertyName("caps")] string[]? Caps = null,
  [property: JsonPropertyName("auth")] AuthInfo? Auth = null,
  [property: JsonPropertyName("role")] string? Role = null,
  [property: JsonPropertyName("scopes")] string[]? Scopes = null,
  [property: JsonPropertyName("device")] DeviceProof? Device = null
);

public sealed record ClientInfo(
  [property: JsonPropertyName("id")] string Id,
  [property: JsonPropertyName("version")] string Version,
  [property: JsonPropertyName("platform")] string Platform,
  [property: JsonPropertyName("mode")] string Mode,
  [property: JsonPropertyName("instanceId")] string? InstanceId = null
);

public sealed record AuthInfo(
  [property: JsonPropertyName("token")] string? Token = null
);

public sealed record HelloOk(
  [property: JsonPropertyName("type")] string Type,
  [property: JsonPropertyName("protocol")] int Protocol,
  [property: JsonPropertyName("server")] JsonElement Server,
  [property: JsonPropertyName("features")] JsonElement Features,
  [property: JsonPropertyName("snapshot")] JsonElement Snapshot,
  [property: JsonPropertyName("canvasHostUrl")] string? CanvasHostUrl,
  [property: JsonPropertyName("auth")] HelloAuthInfo? Auth,
  [property: JsonPropertyName("policy")] JsonElement Policy
);

public sealed record HelloAuthInfo(
  [property: JsonPropertyName("role")] string? Role,
  [property: JsonPropertyName("scopes")] string[]? Scopes,
  [property: JsonPropertyName("deviceToken")] string? DeviceToken
);
