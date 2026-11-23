using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Plico.Desktop;

/// <summary>
/// Captures the Windows caption and frame directly from Plico's window for light and dark theme verification.
/// </summary>
internal static partial class NativeVerification
{
    private const int ExtendedFrameBoundsAttribute = 9;
    private const uint PrintFullContent = 2;
    private const int BytesPerPixel = 4;

    private static async Task CaptureFrameAsync(Window window, string directory, string filename)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND", Handle: var handle }) return;
        try
        {
            // PrintWindow can send drawing messages back to the UI thread.
            using var frame = await Task.Run(() => CopyFrame(handle));
            if (frame is null) return;
            frame.Save(Path.Combine(directory, filename), PngBitmapEncoderOptions.Default);
            File.AppendAllText(Path.Combine(directory, CaptureProgressFilename), filename + Environment.NewLine);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or IOException or ExternalException)
        {
            // The frame capture is supporting evidence; the client-area captures remain the journey's record.
        }
    }

    /// <summary>Renders the window into a private bitmap and crops the invisible resize border. O(w × h) time and space.</summary>
    private static WriteableBitmap? CopyFrame(IntPtr window)
    {
        if (!GetWindowRect(window, out var bounds) ||
            DwmGetWindowAttribute(window, ExtendedFrameBoundsAttribute, out var visible, Marshal.SizeOf<ScreenRect>()) != 0) return null;
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) return null;
        var crop = new PixelRect(visible.Left - bounds.Left, visible.Top - bounds.Top, visible.Right - visible.Left, visible.Bottom - visible.Top)
            .Intersect(new PixelRect(0, 0, width, height));
        if (crop.Width <= 0 || crop.Height <= 0) return null;
        var header = new BitmapHeader
        {
            Size = Marshal.SizeOf<BitmapHeader>(), Width = width, Height = -height, Planes = 1, BitCount = BytesPerPixel * 8
        };
        IntPtr memory = CreateCompatibleDC(IntPtr.Zero);
        if (memory == IntPtr.Zero) return null;
        IntPtr bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            bitmap = CreateDIBSection(memory, ref header, 0, out var pixels, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero) return null;
            previous = SelectObject(memory, bitmap);
            if (previous == IntPtr.Zero || !PrintWindow(window, memory, PrintFullContent) || !GdiFlush()) return null;
            int stride = checked(width * BytesPerPixel);
            var firstPixel = pixels + checked(crop.Y * stride + crop.X * BytesPerPixel);
            return new WriteableBitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, firstPixel, crop.Size, new Vector(96, 96), stride);
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            DeleteDC(memory);
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out ScreenRect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr window, IntPtr target, uint flags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr context);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr context, ref BitmapHeader header, uint usage, out IntPtr pixels, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr context, IntPtr item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GdiFlush();

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr context);
}
