using ModelContextProtocol.Server;
using OpenClaw.McpServer;
using OpenClaw.McpServer.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<McpOpenClawOptions>(builder.Configuration.GetSection("OpenClaw"));
builder.Services.AddSingleton<OpenClawGatewayService>();

// MCP Server over Streamable HTTP
builder.Services.AddMcpServer()
  .WithHttpTransport()
  .WithToolsFromAssembly();

var app = builder.Build();

// Simple shared-key auth (same as ProxyApi)
app.UseMiddleware<ApiKeyAuthMiddleware>();

// Primary MCP endpoint using Streamable HTTP transport.
app.MapMcp("/mcp");

// Pre-connect to gateway at startup (same pattern as ProxyApi)
// so the WebSocket is already Open before any tool call arrives.
app.Lifetime.ApplicationStarted.Register(() =>
{
  _ = Task.Run(async () =>
  {
    try
    {
      var gw = app.Services.GetRequiredService<OpenClawGatewayService>();
      await gw.EnsureConnectedAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
      app.Logger.LogWarning(ex, "Gateway pre-connect failed at startup; tools will retry on first call.");
    }
  });
});

app.Run();
