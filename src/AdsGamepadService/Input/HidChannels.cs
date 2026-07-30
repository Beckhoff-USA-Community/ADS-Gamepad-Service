using System.Runtime.InteropServices;
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

        /* TRUE when the open scan saw a Bluetooth node for the pad, whether
           or not it was chosen. The frozen stream retry only helps when a
           Bluetooth node exists to switch to. */
        bool BluetoothAvailable { get; }

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

        private WindowsHidChannel(SafeFileHandle read, SafeFileHandle write, bool isBluetooth, bool bluetoothAvailable)
        {
            _read = read;
            _write = write;
            IsBluetooth = isBluetooth;
            BluetoothAvailable = bluetoothAvailable;
        }

        public bool IsBluetooth { get; }

        public bool BluetoothAvailable { get; }

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
            return new WindowsHidChannel(read, write, isBluetooth, btPath is not null);
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
       carry the reports. The only interop is a single libc ioctl for the
       feature report read the Bluetooth mode switch needs. */
    [UnsupportedOSPlatform("windows")]
    internal sealed partial class LinuxHidChannel : IHidChannel
    {
        private readonly FileStream _read;
        private readonly FileStream _write;
        private readonly SafeFileHandle _writeHandle;
        private readonly object _writeSync = new();

        public bool IsBluetooth { get; }

        public bool BluetoothAvailable { get; }

        /* HIDIOCGFEATURE from linux/hidraw.h: a read and write ioctl in
           group 'H', number 7, with the buffer size in the size field. */
        internal static nuint FeatureRequestCode(int bufferLength)
        {
            return 0xC0000000u | ((nuint)(bufferLength & 0x3FFF) << 16) | (0x48u << 8) | 0x07u;
        }

        /* The soname, not "libc": the runtime probes dlopen with the exact
           name plus lib/.so decorations, and on glibc none of those exist as
           loadable files. Only the versioned soname is in the loader cache. */
        [LibraryImport("libc.so.6", SetLastError = true)]
        private static partial int ioctl(SafeFileHandle fd, nuint request, byte[] buffer);

        public bool TryReadFeature(byte[] buffer)
        {
            lock (_writeSync)
            {
                if (_writeHandle.IsInvalid || _writeHandle.IsClosed)
                {
                    return false;
                }
                try
                {
                    return ioctl(_writeHandle, FeatureRequestCode(buffer.Length), buffer) >= 0;
                }
                catch (Exception ex) when (ex is ObjectDisposedException or DllNotFoundException or EntryPointNotFoundException)
                {
                    /* A resolution failure must read as a failed feature
                       read, not kill the reader thread and with it the
                       whole service. */
                    return false;
                }
            }
        }

        private LinuxHidChannel(FileStream read, FileStream write, bool isBluetooth, bool bluetoothAvailable)
        {
            _read = read;
            _write = write;
            _writeHandle = write.SafeFileHandle;
            IsBluetooth = isBluetooth;
            BluetoothAvailable = bluetoothAvailable;
        }

        /* USB is preferred like on Windows; the caller flips the preference
           after detecting a frozen USB stream, the signature of a pad whose
           live session is the Bluetooth one. */
        internal static (string Node, bool IsBluetooth)? ChooseNode(string? usbNode, string? bluetoothNode, bool preferBluetooth)
        {
            string? node = preferBluetooth ? bluetoothNode ?? usbNode : usbNode ?? bluetoothNode;
            if (node is null)
            {
                return null;
            }
            return (node, node == bluetoothNode);
        }

        /* The kernel lists every hidraw node under /sys/class/hidraw with a
           uevent file naming the device id as HID_ID=<bus>:<vid>:<pid>.
           Bus 0003 is USB and bus 0005 is Bluetooth, so each transport has
           its own match and neither can take the other's node. */
        internal static LinuxHidChannel? Open(string usbHidIdMatch, string bluetoothHidIdMatch, bool preferBluetooth = false)
        {
            string? usbNode = null;
            string? btNode = null;
            try
            {
                if (Directory.Exists("/sys/class/hidraw"))
                {
                    foreach (string entry in Directory.GetDirectories("/sys/class/hidraw"))
                    {
                        string uevent = Path.Combine(entry, "device", "uevent");
                        try
                        {
                            if (!File.Exists(uevent))
                            {
                                continue;
                            }
                            string content = File.ReadAllText(uevent);
                            if (usbNode is null && content.Contains(usbHidIdMatch, StringComparison.OrdinalIgnoreCase))
                            {
                                usbNode = "/dev/" + Path.GetFileName(entry);
                            }
                            else if (btNode is null && content.Contains(bluetoothHidIdMatch, StringComparison.OrdinalIgnoreCase))
                            {
                                btNode = "/dev/" + Path.GetFileName(entry);
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
            (string Node, bool IsBluetooth)? choice = ChooseNode(usbNode, btNode, preferBluetooth);
            if (choice is null)
            {
                return null;
            }
            string node = choice.Value.Node;

            try
            {
                var read = new FileStream(node, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1);
                try
                {
                    var write = new FileStream(node, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, bufferSize: 1);
                    return new LinuxHidChannel(read, write, choice.Value.IsBluetooth, btNode is not null);
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

        /* Like the Windows channel, deliberately does not take the write
           lock: the feature read ioctl can wait seconds for a silent
           Bluetooth pad, and waiting behind it here would stall shutdown.
           The handle refcount defers the real close until an in-flight call
           returns, and the callers catch the disposed exceptions. */
        public void Dispose()
        {
            _read.Dispose();
            _write.Dispose();
        }
    }
}
