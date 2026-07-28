using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AdsGamepadService.Input
{
    /* Minimal raw HID interop for the PlayStation backend. Only the calls the
       service needs are imported, directly against the Windows API, so the
       shipped service keeps zero third party dependencies just like the
       XInput backend. */
    internal static partial class HidNative
    {
        // Device interface class of every HID device, from hidclass.h
        internal static readonly Guid HidInterfaceGuid = new("4D1E55B2-F16F-11CF-88CB-001111000030");

        private const int CR_SUCCESS = 0;
        private const int CR_BUFFER_SMALL = 26;
        private const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;

        internal const uint GENERIC_READ = 0x80000000;
        internal const uint GENERIC_WRITE = 0x40000000;
        internal const uint FILE_SHARE_READ = 0x00000001;
        internal const uint FILE_SHARE_WRITE = 0x00000002;
        internal const uint OPEN_EXISTING = 3;

        [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_List_SizeW")]
        private static partial int CM_Get_Device_Interface_List_Size(out uint size, in Guid interfaceClassGuid, nint deviceId, uint flags);

        /* The buffer is UTF-16 text, declared as ushort so the generated
           marshalling stays blittable; the caller reinterprets it as chars. */
        [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_ListW")]
        private static partial int CM_Get_Device_Interface_List(in Guid interfaceClassGuid, nint deviceId, [Out] ushort[] buffer, uint bufferLength, uint flags);

        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ReadFile(SafeFileHandle handle, [Out] byte[] buffer, uint bytesToRead, out uint bytesRead, nint overlapped);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool WriteFile(SafeFileHandle handle, byte[] buffer, uint bytesToWrite, out uint bytesWritten, nint overlapped);

        /* Lists the device interface paths of every HID device currently
           present. The size can grow between the two calls when a device
           arrives at exactly the wrong moment, so the sequence retries. */
        internal static string[] ListHidInterfacePaths()
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (CM_Get_Device_Interface_List_Size(out uint length, in HidInterfaceGuid, 0, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS || length < 2)
                {
                    return Array.Empty<string>();
                }

                var buffer = new ushort[length];
                int result = CM_Get_Device_Interface_List(in HidInterfaceGuid, 0, buffer, length, CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
                if (result == CR_BUFFER_SMALL)
                {
                    continue;
                }
                if (result != CR_SUCCESS)
                {
                    return Array.Empty<string>();
                }

                return new string(System.Runtime.InteropServices.MemoryMarshal.Cast<ushort, char>(buffer)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            }
            return Array.Empty<string>();
        }
    }
}
