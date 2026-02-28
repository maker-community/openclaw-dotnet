using ModelContextProtocol.Server;
using OpenClaw.GatewayClient;
using System.ComponentModel;
using System.Text.Json;

namespace OpenClaw.McpServer.Tools;

[McpServerToolType]
public sealed class OpenClawTools
{
  private readonly OpenClawGatewayService _gw;

  public OpenClawTools(OpenClawGatewayService gw) => _gw = gw;

  public sealed record CallResult(bool ok, JsonElement? payload);

  [McpServerTool, Description("Call any OpenClaw gateway method (full access). Params must be a JSON object.")]
  public async Task<CallResult> OpenClawCall(
    [Description("Gateway method name, e.g. sessions.list, chat.send, tools.invoke")] string method,
    [Description("Params JSON object. Example: {\"limit\":10}")] JsonElement @params,
    CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);
    var payload = await _gw.Client.CallRawAsync(method, @params, ct);
    return new CallResult(true, payload);
  }

  public sealed record ChatSendResult(bool ok, JsonElement? payload);

  [McpServerTool, Description("Send a chat message via OpenClaw (wrapper around chat.send). Requires sessionKey + idempotencyKey on this OpenClaw version.")]
  public async Task<ChatSendResult> OpenClawChatSend(
    [Description("OpenClaw sessionKey, e.g. agent:main:main")]
    string sessionKey,
    [Description("Idempotency key for de-duplication. Recommend UUID.")]
    string idempotencyKey,
    [Description("Message text to send.")]
    string message,
    CancellationToken ct)
  {
    await _gw.EnsureConnectedAsync(ct);

    var obj = new Dictionary<string, object?>
    {
      ["sessionKey"] = sessionKey,
      ["idempotencyKey"] = idempotencyKey,
      ["message"] = message
    };

    var payload = await _gw.Client.CallRawAsync("chat.send", obj, ct);
    return new ChatSendResult(true, payload);
  }

  [McpServerTool, Description("Health check for the MCP server (does not call OpenClaw).")]
  public object Health() => new { ok = true };
}
