using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwinCAT.Ads;

namespace AdsGamepadService
{
    public class ServerWorker : BackgroundService
    {
        private readonly ILogger<ServerWorker> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IOptions<ServiceOptions> _options;

        public ServerWorker(ILogger<ServerWorker> logger, ILoggerFactory loggerFactory, IOptions<ServiceOptions> options)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _options = options;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ServiceOptions options;
            try
            {
                options = _options.Value;
            }
            catch (Exception ex)
            {
                /* A value of the wrong type throws inside the options binder.
                   Exit nonzero so the SCM sees a failure and recovery fires,
                   instead of the host stopping cleanly with exit code zero. */
                _logger.LogCritical(ex, "Configuration could not be read.");
                Environment.Exit(1);
                return;
            }

            string[] configErrors = options.Validate();
            if (configErrors.Length > 0)
            {
                foreach (string error in configErrors)
                {
                    _logger.LogCritical("Configuration error: {Error}", error);
                }
                Environment.Exit(1);
                return;
            }

            try
            {
                using var server = new AdsControllerServer((ushort)options.AmsPort, options.ServerName, _loggerFactory, options.MaxControllers, options.SlotSources);
                _logger.LogInformation(
                    "Starting ADS server {Name} on ADS port {Port} with {Controllers} controller slots.",
                    options.ServerName, options.AmsPort, options.MaxControllers);
                AdsErrorCode result = await server.ConnectServerAndWaitAsync(stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("ADS server closed down.");
                    return;
                }

                /* The server library reports most failures through its result
                   code instead of an exception, for example when the local
                   ADS router restarts or refuses the registration. Any
                   completion without a stop request means the server is gone,
                   so exit nonzero and let service recovery restart us. */
                _logger.LogCritical("The ADS server stopped unexpectedly with result {Result}.", result);
                Environment.Exit(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal stop requested by the host
            }
            catch (Exception ex)
            {
                /* Exiting nonzero lets Windows service recovery restart the
                   process instead of leaving a silently stopped service. */
                _logger.LogCritical(ex, "The ADS server failed.");
                Environment.Exit(1);
            }
        }
    }
}
