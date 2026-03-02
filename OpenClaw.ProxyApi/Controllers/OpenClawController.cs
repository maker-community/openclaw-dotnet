using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OpenClaw.GatewayClient;

namespace OpenClaw.ProxyApi.Controllers;

[ApiController]
[Route("api/openclaw")]
public sealed class OpenClawController : ControllerBase
{
    private readonly OpenClawGatewayService _gw;

    public OpenClawController(OpenClawGatewayService gw) => _gw = gw;

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
        HttpContext.RequestServices
          .GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<OpenClaw.ProxyApi.Hubs.OpenClawHub>>()
          .Clients.All
          .SendAsync("gatewayResult", new { requestId, method = req.Method, ok = true, payload }, ct);

        return Ok(new { ok = true, requestId, payload });
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { ok = true });
}
