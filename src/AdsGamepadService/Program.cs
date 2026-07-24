using AdsGamepadService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.EventLog;

/* The content root is pinned to the executable directory so the
   appsettings.json next to the service binary is honored no matter which
   directory the process is started from. */
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ADS Gamepad Service";
});

if (OperatingSystem.IsWindows())
{
    /* Without this the Event Log source would default to the assembly name.
       Keeping it equal to the display name means operators find the log
       entries where the documentation says they are. */
    builder.Services.Configure<EventLogSettings>(settings =>
    {
        settings.SourceName = "ADS Gamepad Service";
    });
}

builder.Services.Configure<ServiceOptions>(builder.Configuration.GetSection(ServiceOptions.SectionName));

builder.Services.AddHostedService<ServerWorker>();

builder.Build().Run();
