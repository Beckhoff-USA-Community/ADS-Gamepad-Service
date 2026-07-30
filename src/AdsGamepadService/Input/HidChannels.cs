using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AdsGamepadService.Input
{
    /* Transport seam between the DualSense logic and the operating system.
       One channel is one opened pad: a blocking report read, a report write,
       and a close. The reading rules are identical on both systems, only the
       way to the device differs. */
    internal interface IHidChannel : IDisposable
    {
        /* Blocks until one input report arrives. Returns the report length,
           or -1 when the device is gone or the channel was closed. */
        int ReadReport(byte[] buffer);

        // Best effort; a lost write on a disconnecting pad is acceptable
        void WriteReport(byte[] report);

        /* TRUE when the pad is reached over Bluetooth, where the report
           framing differs from USB. */
        bool IsBluetooth { get; }

        /* Reads a feature report; buffer[0] names the report id. Returns
           FALSE when the transport does not support it or the read fails. */
        bool TryReadFeature(byte[] buffer);
    }

    /* Windows: raw HID through two separate synchronous handles. On a single
       handle Windows serializes the operations and a write would queue
       behind the blocked read. */
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsHidChannel : IHidChannel
    {
        private readonly SafeFileHandle _read;
        private readonly SafeFileHandle _write;
        private readonly object _writeSync = new();

        private WindowsHidChannel(SafeFileHandle read, SafeFileHandle write, bool isBluetooth)
        {
            _read = read;
            _write = write;
            IsBluetooth = isBluetooth;
        }

        public bool IsBluetooth { get; }

        /* USB is preferred: only the gamepad collection on USB interface
           three of a real pad is accepted there, other USB matches would be
           wrappers or clones with unverified reports. Without a USB pad the
           Bluetooth HID node is taken instead; its path carries the vendor
           and product id in the Bluetooth path shape, so each transport has
           its own filter and neither can match the other. */
        internal static WindowsHidChannel? Open(string usbMatch, string bluetoothMatch, bool preferBluetooth = false)
        {
            string? usbPath = null;
            string? btPath = null;
            foreach (string candidate in HidNative.ListHidInterfacePaths())
            {
                if (usbPath is null &&
                    candidate.Contains(usbMatch, StringComparison.OrdinalIgnoreCase) &&
                    candidate.Contains("mi_03", StringComparison.OrdinalIgnoreCase))
                {
                    usbPath = candidate;
                }
                else if (btPath is null &&
                    candidate.Contains(bluetoothMatch, StringComparison.OrdinalIgnoreCase))
                {
                    btPath = candidate;
                }
            }
            /* The caller flips the preference after detecting a frozen USB
               stream, the signature of a pad whose live session is the
               Bluetooth one. */
            string? path = preferBluetooth ? btPath ?? usbPath : usbPath ?? btPath;
            if (path is null)
            {
                return null;
            }
            bool isBluetooth = path == btPath;

            SafeFileHandle read = HidNative.CreateFile(
                path, HidNative.GENERIC_READ,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                0, HidNative.OPEN_EXISTING, 0, 0);
            if (read.IsInvalid)
            {
                read.Dispose();
                return null;
            }
            /* The write handle also carries the feature report reads used by
               the Bluetooth mode switch, and the HID feature calls want read
               access on the handle they run on. */
            SafeFileHandle write = HidNative.CreateFile(
                path, HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                0, HidNative.OPEN_EXISTING, 0, 0);
            if (write.IsInvalid)
            {
                write.Dispose();
                read.Dispose();
                return null;
            }
            return new WindowsHidChannel(read, write, isBluetooth);
        }

        public bool TryReadFeature(byte[] buffer)
        {
            lock (_writeSync)
            {
                if (_write.IsInvalid || _write.IsClosed)
                {
                    return false;
                }
                try
                {
                    return HidNative.HidD_GetFeature(_write, buffer, (uint)buffer.Length);
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }
        }

        public int ReadReport(byte[] buffer)
        {
            try
            {
                if (!HidNative.ReadFile(_read, buffer, (uint)buffer.Length, out uint read, 0))
                {
                    return -1;
                }
                return (int)read;
            }
            catch (ObjectDisposedException)
            {
                return -1;
            }
        }

        public void WriteReport(byte[] report)
        {
            lock (_writeSync)
            {
                if (_write.IsInvalid || _write.IsClosed)
                {
                    return;
                }
                try
                {
                    HidNative.WriteFile(_write, report, (uint)report.Length, out _, 0);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        /* Deliberately does not take the write lock: closing the handle is
           how a pending synchronous write on a dying Bluetooth link gets
           cancelled, and waiting for it here would stall shutdown for the
           link timeout instead. The writers catch the resulting exception. */
        public void Dispose()
        {
            _read.Dispose();
            _write.Dispose();
        }
    }

    /* Linux: the hidraw character device delivers exactly one report per
       read and takes one report per write, so two unbuffered FileStreams
       are the whole transport. No interop is needed at all. */
    [UnsupportedOSPlatform("windows")]
    internal sealed class LinuxHidChannel : IHidChannel
    {
        private readonly FileStream _read;
        private readonly FileStream _write;
        private readonly object _writeSync = new();

        /* The Linux side stays USB only until the kernel side of Bluetooth
           exists on the target platform. */
        public bool IsBluetooth => false;

        public bool TryReadFeature(byte[] buffer)
        {
            return false;
        }

        private LinuxHidChannel(FileStream read, FileStream write)
        {
            _read = read;
            _write = write;
        }

        /* The kernel lists every hidraw node under /sys/class/hidraw with a
           uevent file naming the device id as HID_ID=<bus>:<vid>:<pid>. The
           caller passes the id with the USB bus prefix, so a Bluetooth pad,
           which carries bus 0005 and a different report format, never
           matches. */
        internal static LinuxHidChannel? Open(string hidIdMatch)
        {
            string? node = null;
            try
            {
                if (Directory.Exists("/sys/class/hidraw"))
                {
                    foreach (string entry in Directory.GetDirectories("/sys/class/hidraw"))
                    {
                        string uevent = Path.Combine(entry, "device", "uevent");
                        try
                        {
                            if (File.Exists(uevent) && File.ReadAllText(uevent).Contains(hidIdMatch, StringComparison.OrdinalIgnoreCase))
                            {
                                node = "/dev/" + Path.GetFileName(entry);
                                break;
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
            if (node is null)
            {
                return null;
            }

            try
            {
                var read = new FileStream(node, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1);
                try
                {
                    var write = new FileStream(node, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, bufferSize: 1);
                    return new LinuxHidChannel(read, write);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A readable but not writable node must not leak the read stream
                    read.Dispose();
                    return null;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Wrong permissions or the node vanished between listing and open
                return null;
            }
        }

        public int ReadReport(byte[] buffer)
        {
            try
            {
                int n = _read.Read(buffer, 0, buffer.Length);
                return n > 0 ? n : -1;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                return -1;
            }
        }

        public void WriteReport(byte[] report)
        {
            lock (_writeSync)
            {
                try
                {
                    _write.Write(report, 0, report.Length);
                    _write.Flush();
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                }
            }
        }

        public void Dispose()
        {
            _read.Dispose();
            lock (_writeSync)
            {
                _write.Dispose();
            }
        }
    }
}
