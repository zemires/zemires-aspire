using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZitadelApp.AdminConsoleApp;
using ZitadelApp.ServiceDefaults;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// connection string "Endpoint=...;ServiceAccountKey=..." provided by the AppHost (ZitadelResource.ApiConnectionString)
builder.AddZitadelApi("zitadel-api");

builder.Services.AddHostedService<AdminWorker>();

await builder.Build().RunAsync();
