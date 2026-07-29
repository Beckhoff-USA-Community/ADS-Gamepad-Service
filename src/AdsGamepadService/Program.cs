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

/* On Linux under systemd this switches the lifetime to Type=notify
   readiness signaling and journal friendly console output. Outside of
   systemd, exactly like AddWindowsService outside the SCM, it does nothing. */
builder.Services.AddSystemd();

/* Without this the Event Log source would default to the assembly name.
   Keeping it equal to the display name means operators find the log
   entries where the documentation says they are. The platform check is
   repeated inside the callback because the analyzer treats the callback
   as its own method and needs its own guard there. */
if (OperatingSystem.IsWindows())
{
    builder.Services.Configure<EventLogSettings>(settings =>
    {
        if (OperatingSystem.IsWindows())
        {
            settings.SourceName = "ADS Gamepad Service";
        }
    });
}

builder.Services.Configure<ServiceOptions>(builder.Configuration.GetSection(ServiceOptions.SectionName));

builder.Services.AddHostedService<ServerWorker>();

builder.Build().Run();
