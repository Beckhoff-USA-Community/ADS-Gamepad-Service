using AdsGamepadService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ADS Gamepad Service";
});

builder.Services.AddHostedService<ServerWorker>();

builder.Build().Run();
