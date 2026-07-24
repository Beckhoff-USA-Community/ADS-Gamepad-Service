using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwinCAT.Ads;

namespace AdsGamepadService
{
    public class ServerWorker : BackgroundService
    {
        /* The ADS port and server name are the identity every released PLC
           library binds to. They move into a configuration file in the
           Windows service phase; until then they stay exactly as shipped. */
        private const ushort AdsPort = 25733;
        private const string AdsPortName = "XboxAdsServer";

        private readonly ILogger<ServerWorker> _logger;
        private readonly ILoggerFactory _loggerFactory;

        public ServerWorker(ILogger<ServerWorker> logger, ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var server = new AdsControllerServer(AdsPort, AdsPortName, _loggerFactory);
                _logger.LogInformation("Starting ADS server {Name} on ADS port {Port}.", AdsPortName, AdsPort);
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
