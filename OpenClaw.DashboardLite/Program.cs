using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using OpenClaw.DashboardLite;
using OpenClaw.DashboardLite.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

builder.Services.AddScoped<SettingsService>();

// HttpClient base address is resolved at call-time (we create a new HttpClient per scope on demand)
// Default points to localhost; user can override in UI and it is stored in localStorage.
builder.Services.AddScoped(sp =>
{
  // Use placeholder; pages will create request with absolute URL when needed.
  return new HttpClient();
});

await builder.Build().RunAsync();
