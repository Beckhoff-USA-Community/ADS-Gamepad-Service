namespace AdsGamepadService
{
    /* Runtime settings read from appsettings.json in the service directory.
       The defaults are the values the service has always shipped with. The
       ADS port is the identity the PLC library binds to, so change it only
       together with the matching setting on the PLC side. */
    public sealed class ServiceOptions
    {
        public const string SectionName = "Service";

        // ADS port the server registers with the local ADS router
        public int AmsPort { get; set; } = 25733;

        // Name of the registration, visible in router diagnostics
        public string ServerName { get; set; } = "XboxAdsServer";

        /* Number of controller slots polled through XInput, 1 to 4.
           Slots above this count answer as disconnected. */
        public int MaxControllers { get; set; } = 4;

        public string[] Validate()
        {
            var errors = new List<string>();
            if (AmsPort < 1 || AmsPort > 65535)
            {
                errors.Add($"AmsPort must be between 1 and 65535, the configured value is {AmsPort}.");
            }
            if (string.IsNullOrWhiteSpace(ServerName))
            {
                errors.Add("ServerName must not be empty.");
            }
            if (MaxControllers < 1 || MaxControllers > 4)
            {
                errors.Add($"MaxControllers must be between 1 and 4, the configured value is {MaxControllers}.");
            }
            return errors.ToArray();
        }
    }
}
