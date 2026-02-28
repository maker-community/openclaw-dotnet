using Microsoft.Extensions.Options;

namespace OpenClaw.ProxyApi.Security;

public sealed class ApiKeyAuthMiddleware
{
  private readonly RequestDelegate _next;

  public ApiKeyAuthMiddleware(RequestDelegate next) => _next = next;

  public async Task Invoke(HttpContext ctx, IOptions<OpenClawOptions> opts)
  {
    // Allow CORS preflight without auth
    if (HttpMethods.IsOptions(ctx.Request.Method))
    {
      ctx.Response.StatusCode = StatusCodes.Status204NoContent;
      return;
    }

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
