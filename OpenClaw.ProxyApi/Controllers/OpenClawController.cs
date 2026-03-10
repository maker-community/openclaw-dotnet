using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OpenClaw.GatewayClient;
using OpenClaw.ProxyApi.Hubs;

namespace OpenClaw.ProxyApi.Controllers;

[ApiController]
[Route("api/openclaw")]
public sealed class OpenClawController : ControllerBase
{
    private readonly OpenClawGatewayService _gw;
    private readonly IHubContext<OpenClawHub> _hub;

    public OpenClawController(OpenClawGatewayService gw, IHubContext<OpenClawHub> hub)
    {
        _gw = gw;
        _hub = hub;
    }

    public sealed record CallRequest(string Method, JsonElement? Params, string? RequestId);

    [HttpPost("call")]
    public async Task<IActionResult> Call([FromBody] CallRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Method))
            return BadRequest("method required");

        await _gw.EnsureConnectedAsync(ct);

        // Params 是 JsonElement?，直接传给 client 会变成 object?；这里用 JsonElement 原样传递
        object? p = null;
        if (req.Params is { } pe)
            p = pe;

        // Allow UI to correlate call -> gateway response over SignalR.
        var requestId = string.IsNullOrWhiteSpace(req.RequestId) ? Guid.NewGuid().ToString() : req.RequestId.Trim();

        var payload = await _gw.Client.CallRawAsync(req.Method, p, ct);

        // Broadcast a lightweight "result" event as well.
        // Note: CallRawAsync generates its own gateway request id; we provide requestId for UI correlation.
        await _hub.Clients.All.SendAsync("gatewayResult", new { requestId, method = req.Method, ok = true, payload }, ct);

        return Ok(new { ok = true, requestId, payload });
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions([FromQuery] int? limit, CancellationToken ct)
    {
        await _gw.EnsureConnectedAsync(ct);
        var payload = await _gw.Client.SessionsListAsync(limit, ct);

        var sessions = payload.Sessions?
            .Select(s => new
            {
                sessionKey = FirstNonEmpty(s.SessionKey, s.Key, s.SessionId) ?? (s.IsMain == true ? "main" : null),
                label = FirstNonEmpty(s.Label, s.Title),
                model = s.Model,
                status = s.Status,
                role = s.Role,
                isMain = s.IsMain,
                thinkingLevel = s.ThinkingLevel,
                verboseLevel = s.VerboseLevel,
                sendPolicy = s.SendPolicy,
                settings = s.Settings
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.sessionKey))
            .ToArray() ?? Array.Empty<object>();

        return Ok(new { ok = true, payload = new { sessions } });
    }

    [HttpGet("sessions/{sessionKey}/history")]
    public async Task<IActionResult> SessionHistory([FromRoute] string sessionKey, [FromQuery] int? limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
            return BadRequest("sessionKey required");

        await _gw.EnsureConnectedAsync(ct);
        try
        {
            var payload = await _gw.Client.SessionsHistoryAsync(sessionKey, limit, before: null, ct);
            return Ok(new { ok = true, payload });
        }
        catch (InvalidOperationException ex) when (HasMissingScope(ex, "operator.admin"))
        {
            // sessions.history may require elevated scope on some gateway builds;
            // fall back to chat.history for regular operator.read/write clients.
            var payload = await _gw.Client.ChatHistoryAsync(sessionKey, limit, ct);
            return Ok(new { ok = true, payload, source = "chat.history" });
        }
        catch (InvalidOperationException ex) when (TryExtractMissingScope(ex, out var scope))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                ok = false,
                error = "missing scope",
                requiredScope = scope
            });
        }
    }

    public sealed record ChatSendRequest(string SessionKey, string Message, string? IdempotencyKey);

    [HttpPost("chat/send")]
    public async Task<IActionResult> ChatSend([FromBody] ChatSendRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.SessionKey))
            return BadRequest("sessionKey required");
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest("message required");

        await _gw.EnsureConnectedAsync(ct);
        var payload = await _gw.Client.ChatSendAsync(req.SessionKey.Trim(), req.Message, req.IdempotencyKey, ct);
        return Ok(new { ok = true, payload });
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { ok = true });

    private static bool HasMissingScope(InvalidOperationException ex, string scope)
        => ex.Message.Contains($"missing scope: {scope}", StringComparison.OrdinalIgnoreCase);

    private static bool TryExtractMissingScope(InvalidOperationException ex, out string scope)
    {
        const string marker = "missing scope:";
        var idx = ex.Message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            scope = string.Empty;
            return false;
        }

        scope = ex.Message[(idx + marker.Length)..].Trim();
        return !string.IsNullOrWhiteSpace(scope);
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
