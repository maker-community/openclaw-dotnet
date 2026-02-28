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

app.MapMcp();

app.Run();
