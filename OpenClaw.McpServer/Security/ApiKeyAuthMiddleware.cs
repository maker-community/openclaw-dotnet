using Microsoft.Extensions.Options;

namespace OpenClaw.McpServer.Security;

public sealed class ApiKeyAuthMiddleware
{
  private readonly RequestDelegate _next;

  public ApiKeyAuthMiddleware(RequestDelegate next) => _next = next;

  public async Task Invoke(HttpContext ctx, IOptions<McpOpenClawOptions> opts)
  {
    var apiKey = opts.Value.ApiKey;
    if (string.IsNullOrWhiteSpace(apiKey))
    {
      await _next(ctx);
      return;
    }

    var header = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (!string.Equals(header, apiKey, StringComparison.Ordinal))
    {
      ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
      await ctx.Response.WriteAsync("missing/invalid X-Api-Key");
      return;
    }

    await _next(ctx);
  }
}
