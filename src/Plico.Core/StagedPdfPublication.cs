using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Plico.Core;

/// <summary>Publishes a completed, same-filesystem PDF while preserving occupied names.</summary>
internal static class StagedPdfPublication
{
    private const string LinuxCLibrary = "libc";
    private const string MacSystemLibrary = "libSystem.B.dylib";
    private const int NameExists = 17;
    private const int InvalidArgument = 22;
    private const int LinuxNotImplemented = 38;
    private const int LinuxNotSupported = 95;
    private const int MacNotSupported = 45;
    private const int CurrentLinuxDirectory = -100;
    private const uint LinuxNoReplace = 1;
    private const uint MacRenameExclusive = 4;

    /// <summary>Claims the destination for the staged PDF, or returns false when its name is occupied.</summary>
    internal static bool TryPublish(string stagedPdf, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                File.Move(stagedPdf, destination, overwrite: false);
                return true;
            }
            catch (Exception error) when ((error is IOException or UnauthorizedAccessException) && Path.Exists(destination))
            {
                return false;
            }
        }

        // The managed Unix move can check for the destination and rename in separate system calls.
        int errorCode = OperatingSystem.IsMacOS()
            ? PublishOnMac(stagedPdf, destination)
            : PublishOnLinux(stagedPdf, destination);
        if (errorCode == 0) return true;
        if (errorCode == NameExists || Path.Exists(destination)) return false;
        var errorDetails = new Win32Exception(errorCode);
        throw new IOException($"Could not publish the PDF in the export folder. {errorDetails.Message}", errorDetails);
    }

    private static int PublishOnLinux(string stagedPdf, string destination)
    {
        int errorCode;
        try
        {
            if (RenameOnLinux(CurrentLinuxDirectory, stagedPdf, CurrentLinuxDirectory, destination, LinuxNoReplace) == 0) return 0;
            errorCode = Marshal.GetLastPInvokeError();
        }
        catch (EntryPointNotFoundException)
        {
            errorCode = LinuxNotImplemented;
        }

        // A hard link claims a fresh name for the complete PDF if the filesystem lacks exclusive rename.
        if (errorCode is not (LinuxNotImplemented or LinuxNotSupported or InvalidArgument)) return errorCode;
        return LinkOnLinux(stagedPdf, destination) == 0 ? 0 : Marshal.GetLastPInvokeError();
    }

    private static int PublishOnMac(string stagedPdf, string destination)
    {
        if (RenameOnMac(stagedPdf, destination, MacRenameExclusive) == 0) return 0;
        int errorCode = Marshal.GetLastPInvokeError();
        if (errorCode is not (MacNotSupported or InvalidArgument)) return errorCode;
        return LinkOnMac(stagedPdf, destination) == 0 ? 0 : Marshal.GetLastPInvokeError();
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
