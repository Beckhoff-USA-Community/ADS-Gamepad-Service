namespace AdsGamepadService.Input
{
    /* Pure conversion math shared by the gamepad backends.
       The formulas replicate the retired C++ XInput wrapper exactly.
       Changing any of them changes the values every deployed PLC program
       receives, so they are locked by the characterization tests. */
    internal static class GamepadMath
    {
        /* The left stick deadzone constant from Xinput.h. The original wrapper
           applied it to all four stick axes, including the right stick, and
           machines are tuned to that behavior, so it stays that way. */
        internal const int StickDeadzone = 7849;

        internal const int TriggerThreshold = 30;

        // Mask for the XInput path, which never publishes bits 10 and 11
        private const ushort WireButtonMask = 0xF3FF;

        /* A stick axis inside the deadzone reads as zero. Outside it the raw
           value is scaled to percent with no renormalization, so the output
           steps from zero to roughly 24 percent at the deadzone edge. */
        internal static float StickPercent(short raw)
        {
            if (raw > StickDeadzone || raw < -StickDeadzone)
            {
                return (raw / 32768.0f) * 100.0f;
            }
            return 0.0f;
        }

        internal static float TriggerPercent(byte raw)
        {
            if (raw > TriggerThreshold)
            {
                return (raw / 255.0f) * 100.0f;
            }
            return 0.0f;
        }

        /* Percent to motor speed without clamping. Out of range input wraps
           through the integer truncation, matching the old wrapper. The PLC
           library clamps to the range 0 to 100 before sending.

           The old native build converted through a 32 bit integer where NaN
           and values beyond the integer range produced 0x80000000, which
           truncates to motor speed zero. Conversions saturate on current
           .NET, so that conversion is spelled out here to keep even
           adversarial inputs behaving exactly as before. */
        internal static ushort RumbleMotorSpeed(float percent)
        {
            float value = (percent / 100.0f) * 65535.0f;
            int truncated = (float.IsNaN(value) || value >= 2147483648.0f || value < -2147483648.0f)
                ? int.MinValue
                : (int)value;
            return unchecked((ushort)truncated);
        }

        /* Bits 10 and 11 are stripped from the XInput path: bit 10 is the
           Guide button there and was never part of the contract. From
           contract 1.2 on the PlayStation backend publishes Create and
           Options on those bits, built directly without this mask. */
        internal static ushort WireButtons(ushort wButtons)
        {
            return (ushort)(wButtons & WireButtonMask);
        }

        /* Rumble percent to the byte motor range of the PlayStation output
           report. Same philosophy as the speed conversion above: the service
           does not clamp, the conversion goes through a 32 bit integer, and
           out of range input wraps through the final cast. */
        internal static byte RumbleMotorByte(float percent)
        {
            float value = (percent / 100.0f) * 255.0f;
            int truncated = (float.IsNaN(value) || value >= 2147483648.0f || value < -2147483648.0f)
                ? int.MinValue
                : (int)value;
            return unchecked((byte)truncated);
        }
    }
}
