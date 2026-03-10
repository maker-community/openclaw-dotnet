using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenClaw.GatewayClient;

// ─── health ──────────────────────────────────────────────────────────────────

public sealed record HealthResult(
  [property: JsonPropertyName("ok")] bool Ok,
  [property: JsonPropertyName("status")] string? Status,
  [property: JsonPropertyName("gatewayVersion")] string? GatewayVersion,
  [property: JsonPropertyName("uptimeMs")] long? UptimeMs,
  [property: JsonPropertyName("details")] JsonElement? Details
);

// ─── system-presence ─────────────────────────────────────────────────────────

public sealed record PresenceEntry(
  [property: JsonPropertyName("deviceId")] string DeviceId,
  [property: JsonPropertyName("role")] string? Role,
  [property: JsonPropertyName("scopes")] string[]? Scopes,
  [property: JsonPropertyName("clientId")] string? ClientId,
  [property: JsonPropertyName("platform")] string? Platform,
  [property: JsonPropertyName("mode")] string? Mode
);

public sealed record SystemPresenceResult(
  [property: JsonPropertyName("entries")] PresenceEntry[]? Entries
);

// ─── sessions ────────────────────────────────────────────────────────────────

public sealed record SessionInfo(
  [property: JsonPropertyName("sessionKey")] string? SessionKey,
  [property: JsonPropertyName("key")] string? Key,
  [property: JsonPropertyName("sessionId")] string? SessionId,
  [property: JsonPropertyName("label")] string? Label,
  [property: JsonPropertyName("title")] string? Title,
  [property: JsonPropertyName("model")] string? Model,
  [property: JsonPropertyName("status")] string? Status,
  [property: JsonPropertyName("role")] string? Role,
  [property: JsonPropertyName("isMain")] bool? IsMain,
  [property: JsonPropertyName("thinkingLevel")] string? ThinkingLevel,
  [property: JsonPropertyName("verboseLevel")] string? VerboseLevel,
  [property: JsonPropertyName("sendPolicy")] string? SendPolicy,
  [property: JsonPropertyName("settings")] JsonElement? Settings
);

public sealed record SessionsListResult(
  [property: JsonPropertyName("sessions")] SessionInfo[]? Sessions
);

public sealed record MessageEntry(
  [property: JsonPropertyName("role")] string Role,
  [property: JsonPropertyName("content")] JsonElement? Content,
  [property: JsonPropertyName("ts")] long? Ts,
  [property: JsonPropertyName("id")] string? Id,
  [property: JsonPropertyName("metadata")] JsonElement? Metadata
);

public sealed record SessionHistoryResult(
  [property: JsonPropertyName("messages")] MessageEntry[]? Messages,
  [property: JsonPropertyName("hasMore")] bool? HasMore
);

/// <summary>
/// Partial update for a session. Only non-null fields are sent to the gateway.
/// Use <see cref="OpenClawGatewayClient.SessionsPatchAsync"/> which builds the sparse dict automatically.
/// </summary>
public sealed record SessionPatch(
  string? Model = null,
  string? ThinkingLevel = null,
  string? VerboseLevel = null,
  string? SendPolicy = null,
  string? GroupActivation = null,
  bool? Elevated = null
);

// ─── chat.send ───────────────────────────────────────────────────────────────

public sealed record ChatSendResult(
  [property: JsonPropertyName("runId")] string? RunId,
  [property: JsonPropertyName("status")] string? Status,
  [property: JsonPropertyName("queued")] bool? Queued
);

// ─── agent ───────────────────────────────────────────────────────────────────

public sealed record AgentResult(
  [property: JsonPropertyName("runId")] string? RunId,
  [property: JsonPropertyName("status")] string? Status,
  [property: JsonPropertyName("summary")] string? Summary,
  [property: JsonPropertyName("output")] string? Output
);

// ─── tools ───────────────────────────────────────────────────────────────────

public sealed record ToolEntry(
  [property: JsonPropertyName("name")] string Name,
  [property: JsonPropertyName("description")] string? Description,
  [property: JsonPropertyName("source")] string? Source,
  [property: JsonPropertyName("pluginId")] string? PluginId,
  [property: JsonPropertyName("optional")] bool? Optional,
  [property: JsonPropertyName("schema")] JsonElement? Schema
);

public sealed record ToolsCatalogResult(
  [property: JsonPropertyName("tools")] ToolEntry[]? Tools
);

// ─── nodes ───────────────────────────────────────────────────────────────────

public sealed record NodeInfo(
  [property: JsonPropertyName("deviceId")] string DeviceId,
  [property: JsonPropertyName("clientId")] string? ClientId,
  [property: JsonPropertyName("platform")] string? Platform,
  [property: JsonPropertyName("caps")] string[]? Caps,
  [property: JsonPropertyName("commands")] string[]? Commands,
  [property: JsonPropertyName("permissions")] JsonElement? Permissions
);

public sealed record NodeListResult(
  [property: JsonPropertyName("nodes")] NodeInfo[]? Nodes
);
