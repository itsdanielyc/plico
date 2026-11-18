using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using NAPS2.Images;
using NAPS2.Images.Skia;
using NAPS2.Images.Transforms;
using NAPS2.Scan;
using SkiaSharp;

namespace Plico.Core;

/// <summary>Turns confidently detected sideways pages before writing their visible and searchable layers.</summary>
internal static class TesseractOrientation
{
    private const string ModelName = "osd.traineddata";
    private const string RotationPrefix = "Rotate:";
    private const string ConfidencePrefix = "Orientation confidence:";
    private const double MinimumConfidence = 15.0;
    private const int MaximumSampleEdge = 1_200;
    private const int MinimumUsefulWords = 6;
    private const int MinimumWordLength = 3;
    private const int MaximumWordWeight = 12;
    private const double MinimumWordConfidence = 60;
    private const double MinimumScoreLead = 1.25;
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(25);
    private static readonly int[] QuarterTurns = [0, 90, 180, 270];
    private static int _osdUnavailable;

    private sealed record ProbeOutput(int ExitCode, string Text, string Diagnostics);
    private readonly record struct OrientationScore(int Degrees, int Words, double Weight);

    internal static async Task<ICollection<ProcessedImage>> OrientAsync(ScanningContext context,
        ICollection<ProcessedImage> pages, string models, string language, CancellationToken cancellationToken)
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
                using var rendered = (SkiaImage)context.ImageContext.Render(page);
                int rotation = await DetectAsync(executable, rendered, input, models, language, cancellationToken).ConfigureAwait(false);
                oriented.Add(rotation == 0 ? page : page.WithTransform(new RotationTransform(rotation), disposeSelf: true));
            }
            finally
            {
                File.Delete(input);
            }
        }
        return oriented;
    }

    private static async Task<int> DetectAsync(string executable, SkiaImage rendered, string input, string models, string language,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _osdUnavailable) == 0)
        {
            rendered.Save(input, ImageFileFormat.Png);
            var result = await RunTesseractAsync(executable, input, models, ["--dpi", "300", "--psm", "0"], cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode == 0) return ReadRotation(result.Text);
            if (!result.Diagnostics.Contains("OSD Only", StringComparison.OrdinalIgnoreCase) &&
                !result.Diagnostics.Contains("legacy engine", StringComparison.OrdinalIgnoreCase)) return 0;
            Volatile.Write(ref _osdUnavailable, 1);
        }

        return await EstimateFromRecognitionAsync(executable, rendered.Bitmap, input, models, language, cancellationToken)
            .ConfigureAwait(false);
    }

    // Four recognition passes at most, over a bounded raster. The temporary file is reused between turns.
    internal static async Task<int> EstimateFromRecognitionAsync(string executable, SKBitmap bitmap, string input, string models,
        string language, CancellationToken cancellationToken)
    {
        using var raster = SKImage.FromBitmap(bitmap);
        OrientationScore best = default;
        double secondWeight = 0;
        foreach (int degrees in QuarterTurns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteSample(raster, input, degrees);
            var result = await RunTesseractAsync(executable, input, models,
                    ["-l", language, "--dpi", "160", "--psm", "11", "-c", "tessedit_create_tsv=1"], cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0) return 0;
            var score = ScoreSample(result.Text, degrees);
            if (score.Weight > best.Weight)
            {
                secondWeight = best.Weight;
                best = score;
            }
            else
            {
                secondWeight = Math.Max(secondWeight, score.Weight);
            }
        }
        return best.Words >= MinimumUsefulWords && best.Weight > secondWeight * MinimumScoreLead ? best.Degrees : 0;
    }

    private static void WriteSample(SKImage raster, string input, int degrees)
    {
        double scale = Math.Min(1, (double)MaximumSampleEdge / Math.Max(raster.Width, raster.Height));
        int sampleWidth = Math.Max(1, (int)Math.Round(raster.Width * scale));
        int sampleHeight = Math.Max(1, (int)Math.Round(raster.Height * scale));
        bool swapAxes = degrees is 90 or 270;
        using var sample = new SKBitmap(swapAxes ? sampleHeight : sampleWidth, swapAxes ? sampleWidth : sampleHeight);
        using (var canvas = new SKCanvas(sample))
        {
            canvas.Clear(SKColors.White);
            canvas.Translate(sample.Width / 2f, sample.Height / 2f);
            canvas.RotateDegrees(degrees);
            var frame = SKRect.Create(-sampleWidth / 2f, -sampleHeight / 2f, sampleWidth, sampleHeight);
            canvas.DrawImage(raster, frame, new SKSamplingOptions(SKFilterMode.Linear));
        }
        using var png = sample.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new IOException("Plico could not encode an orientation sample.");
        using var output = File.Create(input);
        png.SaveTo(output);
    }

    private static OrientationScore ScoreSample(string tsv, int degrees)
    {
        int words = 0;
        double weight = 0;
        using var rows = new StringReader(tsv);
        while (rows.ReadLine() is { } row)
        {
            if (!row.StartsWith("5\t", StringComparison.Ordinal)) continue;
            string[] columns = row.Split('\t', count: 12);
            if (columns.Length != 12 || !double.TryParse(columns[10], CultureInfo.InvariantCulture, out double confidence) ||
                confidence <= MinimumWordConfidence || confidence > 100) continue;
            int glyphs = columns[11].Count(char.IsLetterOrDigit);
            if (glyphs < MinimumWordLength) continue;
            words++;
            weight += (confidence - MinimumWordConfidence) * Math.Min(glyphs, MaximumWordWeight);
        }
        return new OrientationScore(degrees, words, weight);
    }

    private static async Task<ProbeOutput> RunTesseractAsync(string executable, string input, string models,
        IReadOnlyList<string> options, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { input, "stdout", "--tessdata-dir", models })
            start.ArgumentList.Add(argument);
        foreach (string argument in options)
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
            return new ProbeOutput(process.ExitCode, await output.ConfigureAwait(false), await diagnostics.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new ProbeOutput(-1, "", "");
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

    internal static string FindExecutable()
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
