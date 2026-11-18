using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using NAPS2.Images;
using NAPS2.Images.Transforms;
using NAPS2.Scan;

namespace Plico.Core;

/// <summary>Turns confidently detected sideways pages before writing their visible and searchable layers.</summary>
internal static class TesseractOrientation
{
    private const string ModelName = "osd.traineddata";
    private const string RotationPrefix = "Rotate:";
    private const string ConfidencePrefix = "Orientation confidence:";
    private const double MinimumConfidence = 15.0;
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(25);

    internal static async Task<ICollection<ProcessedImage>> OrientAsync(ScanningContext context,
        ICollection<ProcessedImage> pages, string models, CancellationToken cancellationToken)
    {
        if (!File.Exists(Path.Combine(models, ModelName)))
            throw new FileNotFoundException("Plico's page orientation model is missing. Extract a fresh copy of Plico.", ModelName);
        string executable = FindExecutable();
        var oriented = new List<ProcessedImage>(pages.Count);
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = Path.Combine(context.TempFolderPath, Path.GetRandomFileName() + ".png");
            try
            {
                using (var rendered = context.ImageContext.Render(page)) rendered.Save(input, ImageFileFormat.Png);
                int rotation = await DetectAsync(executable, input, models, cancellationToken).ConfigureAwait(false);
                oriented.Add(rotation == 0 ? page : page.WithTransform(new RotationTransform(rotation), disposeSelf: true));
            }
            finally
            {
                File.Delete(input);
            }
        }
        return oriented;
    }

    private static async Task<int> DetectAsync(string executable, string input, string models, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { input, "stdout", "--tessdata-dir", models, "--dpi", "300", "--psm", "0" })
            start.ArgumentList.Add(argument);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(PageTimeout);
        using var process = Process.Start(start) ?? throw new IOException("Plico could not start Tesseract for orientation detection.");
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var diagnostics = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(output, diagnostics).ConfigureAwait(false);
            return process.ExitCode == 0 ? ReadRotation(await output.ConfigureAwait(false)) : 0;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return 0;
        }
    }

    internal static int ReadRotation(string output)
    {
        int rotation = 0;
        double confidence = 0;
        using var lines = new StringReader(output);
        while (lines.ReadLine() is { } line)
        {
            if (line.StartsWith(RotationPrefix, StringComparison.Ordinal))
                int.TryParse(line.AsSpan(RotationPrefix.Length), CultureInfo.InvariantCulture, out rotation);
            if (line.StartsWith(ConfidencePrefix, StringComparison.Ordinal))
                double.TryParse(line.AsSpan(ConfidencePrefix.Length), CultureInfo.InvariantCulture, out confidence);
        }
        return confidence >= MinimumConfidence && rotation is 90 or 180 or 270 ? rotation : 0;
    }

    private static string FindExecutable()
    {
        bool arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        string directory = OperatingSystem.IsWindows() ? (arm ? "_winarm" : "_win64") :
            OperatingSystem.IsMacOS() ? (arm ? "_macarm" : "_mac") : (arm ? "_linuxarm" : "_linux");
        string name = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
        string bundled = Path.Combine(AppContext.BaseDirectory, directory, name);
        if (File.Exists(bundled)) return bundled;
        string flat = Path.Combine(AppContext.BaseDirectory, name);
        return File.Exists(flat) ? flat : throw new FileNotFoundException("Plico's bundled Tesseract executable is missing.", bundled);
    }
}
