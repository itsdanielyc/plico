using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Plico.Desktop;

/// <summary>
/// Evidence of what only the screen shows: the native caption and frame that <c>RenderTargetBitmap</c> leaves out.
/// On Windows it copies the window's visible frame from the screen, so the Base-tinted caption, its text and the seam
/// against the sidebar can be checked in light and dark. Elsewhere, and when the copy fails, it records nothing.
/// </summary>
internal static partial class NativeVerification
{
    private const int ExtendedFrameBoundsAttribute = 9;
    private const int SourceCopy = 0x00CC0020;
    private const int CaptureLayered = 0x40000000;

    private static async Task CaptureFrameAsync(Window window, string directory, string filename)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND", Handle: var handle }) return;
        window.Activate();
        // Let the window come forward and the compositor present the current frame before copying the screen.
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        try
        {
            if (CopyFrame(handle) is not { } frame) return;
            using (frame)
                frame.Save(Path.Combine(directory, filename), PngBitmapEncoderOptions.Default);
            File.AppendAllText(Path.Combine(directory, CaptureProgressFilename), filename + Environment.NewLine);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or IOException or ExternalException)
        {
            // The frame capture is supporting evidence; the client-area captures remain the journey's record.
        }
    }

    /// <summary>Copies the window's visible frame (caption included) from the screen into a bitmap. O(w × h).</summary>
    private static WriteableBitmap? CopyFrame(IntPtr window)
    {
        if (DwmGetWindowAttribute(window, ExtendedFrameBoundsAttribute, out var bounds, Marshal.SizeOf<ScreenRect>()) != 0) return null;
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) return null;
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr bitmap = CreateCompatibleBitmap(screen, width, height);
        IntPtr previous = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, bounds.Left, bounds.Top, SourceCopy | CaptureLayered)) return null;
            SelectObject(memory, previous);
            var header = new BitmapHeader
            {
                Size = Marshal.SizeOf<BitmapHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32, Compression = 0
            };
            var pixels = new byte[width * height * 4];
            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref header, 0) == 0) return null;
            var frame = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var target = frame.Lock())
                for (int row = 0; row < height; row++)
                    Marshal.Copy(pixels, row * width * 4, target.Address + row * target.RowBytes, width * 4);
            return frame;
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    // DllImport keeps the desktop project free of unsafe code; every argument is blittable.
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out ScreenRect value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr context);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr context);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr context, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr context, IntPtr item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int operation);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr context, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapHeader header, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr context);
}
