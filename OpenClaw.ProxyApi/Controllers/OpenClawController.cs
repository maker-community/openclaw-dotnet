using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using OpenClaw.GatewayClient;

namespace OpenClaw.ProxyApi.Controllers;

[ApiController]
[Route("api/openclaw")]
public sealed class OpenClawController : ControllerBase
{
  private readonly OpenClawGatewayService _gw;

  public OpenClawController(OpenClawGatewayService gw) => _gw = gw;

  public sealed record CallRequest(string Method, JsonElement? Params);

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

    var payload = await _gw.Client.CallRawAsync(req.Method, p, ct);
    return Ok(new { ok = true, payload });
  }

  [HttpGet("health")]
  public IActionResult Health() => Ok(new { ok = true });
}
