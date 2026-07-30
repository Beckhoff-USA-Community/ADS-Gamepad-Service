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

        /* Number of active controller slots, 1 to 4. Slots above this count
           answer as disconnected. */
        public int MaxControllers { get; set; } = 4;

        internal const string SourceXInput = "XInput";
        internal const string SourceDualSense = "DualSense";

        /* Input backend per controller slot, in slot order. Missing entries
           default to XInput, the behavior of every release before 2.2.0.
           XInput polls the pad with the same number as the slot, so slot one
           reads XInput index zero. DualSense reads one PlayStation 5
           controller over raw HID, on Windows over USB or Bluetooth with
           the cable preferred; at most one slot can use it, since the
           service opens a single pad. A slot never changes its backend at
           runtime, only through this setting. */
        public string[]? SlotSources { get; set; }

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
            if (SlotSources is not null)
            {
                if (SlotSources.Length > 4)
                {
                    errors.Add($"SlotSources holds {SlotSources.Length} entries, the wire contract has 4 slots.");
                }
                int dualSenseSlots = 0;
                foreach (string source in SlotSources)
                {
                    if (string.Equals(source, SourceDualSense, StringComparison.OrdinalIgnoreCase))
                    {
                        dualSenseSlots++;
                    }
                    else if (!string.Equals(source, SourceXInput, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"SlotSources contains the unknown backend \"{source}\". Allowed values are {SourceXInput} and {SourceDualSense}.");
                    }
                }
                if (dualSenseSlots > 1)
                {
                    errors.Add("Only one slot can use the DualSense backend, the service reads a single pad.");
                }
            }
            return errors.ToArray();
        }
    }
}
