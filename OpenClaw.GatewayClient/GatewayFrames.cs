using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenClaw.GatewayClient;

public sealed record GatewayErrorShape(
  [property: JsonPropertyName("code")] string Code,
  [property: JsonPropertyName("message")] string Message,
  [property: JsonPropertyName("details")] JsonElement? Details = null,
  [property: JsonPropertyName("retryable")] bool? Retryable = null,
  [property: JsonPropertyName("retryAfterMs")] long? RetryAfterMs = null
);

public sealed record RequestFrame(
  [property: JsonPropertyName("type")] string Type,
  [property: JsonPropertyName("id")] string Id,
  [property: JsonPropertyName("method")] string Method,
  [property: JsonPropertyName("params")] object? Params
);

public sealed record ResponseFrame(
  [property: JsonPropertyName("type")] string Type,
  [property: JsonPropertyName("id")] string Id,
  [property: JsonPropertyName("ok")] bool Ok,
  [property: JsonPropertyName("payload")] JsonElement? Payload,
  [property: JsonPropertyName("error")] GatewayErrorShape? Error
);

public sealed record EventFrame(
  [property: JsonPropertyName("type")] string Type,
  [property: JsonPropertyName("event")] string Event,
  [property: JsonPropertyName("payload")] JsonElement? Payload,
  [property: JsonPropertyName("seq")] long? Seq,
  [property: JsonPropertyName("stateVersion")] JsonElement? StateVersion
);
