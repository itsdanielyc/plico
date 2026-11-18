using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Plico.Core;

/// <summary>Returns false when the destination name is occupied. Only a private stage may remain linked after success.</summary>
internal delegate bool TryClaimPdfPath(string source, string destination, bool allowHardLink);

/// <summary>Claims a destination for a complete PDF without replacing an existing entry.</summary>
internal static class PdfFilePublication
{
    private const string LinuxCLibrary = "libc";
    private const string MacSystemLibrary = "libSystem.B.dylib";
    private const int NameExists = 17;
    private const int IsDirectory = 21;
    private const int InvalidArgument = 22;
    private const int LinuxNotImplemented = 38;
    private const int LinuxNotSupported = 95;
    private const int MacNotSupported = 45;
    private const int WindowsFileExists = unchecked((int)0x80070050);
    private const int WindowsAlreadyExists = unchecked((int)0x800700B7);
    private const int CurrentLinuxDirectory = -100;
    private const uint LinuxNoReplace = 1;
    private const uint MacRenameExclusive = 4;

    /// <summary>Claims the destination for the PDF, or returns false when its name is occupied.</summary>
    /// <param name="source">The complete PDF to publish or move.</param>
    /// <param name="destination">The full path to claim.</param>
    /// <param name="allowHardLink">Allows publication to leave a private stage for its owner to clean up.</param>
    internal static bool TryClaim(string source, string destination, bool allowHardLink)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                File.Move(source, destination, overwrite: false);
                return true;
            }
            catch (IOException error) when (error.HResult is WindowsFileExists or WindowsAlreadyExists)
            {
                return false;
            }
            catch (UnauthorizedAccessException) when (Path.Exists(destination))
            {
                return false;
            }
        }

        // The managed Unix move can check for the destination and rename in separate system calls.
        int errorCode = OperatingSystem.IsMacOS()
            ? ClaimOnMac(source, destination, allowHardLink)
            : ClaimOnLinux(source, destination, allowHardLink);
        if (errorCode == 0) return true;
        if (errorCode is NameExists or IsDirectory) return false;
        var errorDetails = new Win32Exception(errorCode);
        throw new IOException($"Could not place the PDF at its destination. {errorDetails.Message}", errorDetails);
    }

    private static int ClaimOnLinux(string source, string destination, bool allowHardLink)
    {
        int errorCode;
        try
        {
            if (RenameOnLinux(CurrentLinuxDirectory, source, CurrentLinuxDirectory, destination, LinuxNoReplace) == 0) return 0;
            errorCode = Marshal.GetLastPInvokeError();
        }
        catch (EntryPointNotFoundException)
        {
            errorCode = LinuxNotImplemented;
        }

        // A live move must remove its original name in the same operation so the move journal can recover it.
        if (!allowHardLink || errorCode is not (LinuxNotImplemented or LinuxNotSupported or InvalidArgument)) return errorCode;
        return LinkOnLinux(source, destination) == 0 ? 0 : Marshal.GetLastPInvokeError();
    }

    private static int ClaimOnMac(string source, string destination, bool allowHardLink)
    {
        if (RenameOnMac(source, destination, MacRenameExclusive) == 0) return 0;
        int errorCode = Marshal.GetLastPInvokeError();
        if (!allowHardLink || errorCode is not (MacNotSupported or InvalidArgument)) return errorCode;
        return LinkOnMac(source, destination) == 0 ? 0 : Marshal.GetLastPInvokeError();
    }

    [DllImport(LinuxCLibrary, EntryPoint = "renameat2", SetLastError = true, ExactSpelling = true)]
    private static extern int RenameOnLinux(int sourceDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        int destinationDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string destination, uint flags);

    [DllImport(MacSystemLibrary, EntryPoint = "renamex_np", SetLastError = true, ExactSpelling = true)]
    private static extern int RenameOnMac([MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string destination, uint flags);

    [DllImport(LinuxCLibrary, EntryPoint = "link", SetLastError = true, ExactSpelling = true)]
    private static extern int LinkOnLinux([MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string destination);

    [DllImport(MacSystemLibrary, EntryPoint = "link", SetLastError = true, ExactSpelling = true)]
    private static extern int LinkOnMac([MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string destination);
}
