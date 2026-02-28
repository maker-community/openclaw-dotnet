using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using OpenClaw.ProxyApi;
using OpenClaw.ProxyApi.Hubs;
using OpenClaw.ProxyApi.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<OpenClawOptions>(builder.Configuration.GetSection("OpenClaw"));

builder.Services.AddSingleton(sp =>
{
  var opts = sp.GetRequiredService<IOptions<OpenClawOptions>>().Value;
  return new OpenClawGatewayService(sp.GetRequiredService<ILogger<OpenClawGatewayService>>(), opts);
});

builder.Services.AddSignalR();

// CORS for separated DashboardLite
var dashboardOrigin = builder.Configuration["OpenClaw:DashboardOrigin"]; // e.g. http://20.189.114.82:5020
if (!string.IsNullOrWhiteSpace(dashboardOrigin))
{
  builder.Services.AddCors(o => o.AddPolicy(Cors.PolicyName, p =>
  {
    p.WithOrigins(dashboardOrigin)
     .AllowAnyMethod()
     .AllowAnyHeader()
     .AllowCredentials();
  }));
}

var app = builder.Build();

// CORS must run before auth so preflight OPTIONS isn't blocked
if (!string.IsNullOrWhiteSpace(app.Configuration["OpenClaw:DashboardOrigin"]))
{
  app.UseCors(Cors.PolicyName);
}

app.UseMiddleware<ApiKeyAuthMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.MapControllers();
app.MapHub<OpenClawHub>("/hub/openclaw");

app.Lifetime.ApplicationStarted.Register(() =>
{
  _ = Task.Run(async () =>
  {
    try
    {
      var gw = app.Services.GetRequiredService<OpenClawGatewayService>();
      var hub = app.Services.GetRequiredService<IHubContext<OpenClawHub>>();
      await gw.EnsureConnectedAsync(CancellationToken.None);

      // NOTE: handler 注册要在确保已连上以后；connect.challenge/hello-ok events 也会被推送。
      gw.Client.OnEvent += (evt) =>
      {
        _ = hub.Clients.All.SendAsync("gatewayEvent", evt);
      };
    }
    catch (Exception ex)
    {
      app.Logger.LogError(ex, "Failed to start OpenClaw gateway bridge");
    }
  });
});

app.Run();
