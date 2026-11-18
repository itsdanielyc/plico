using System.Diagnostics;

namespace Plico.Core;

/// <summary>Runs the local LibreOffice installation with a fresh profile and validates the generated PDF.</summary>
internal static class OfficePdfConverter
{
    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromMinutes(2);
    private const string ProfileConfiguration = """
        <?xml version="1.0" encoding="UTF-8"?>
        <oor:items xmlns:oor="http://openoffice.org/2001/registry">
          <item oor:path="/org.openoffice.Office.Common/Security/Scripting">
            <prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop>
          </item>
          <item oor:path="/org.openoffice.Office.Writer/Content/Update">
            <prop oor:name="Link" oor:op="fuse"><value>2</value></prop>
          </item>
        </oor:items>
        """;

    internal static async Task<string> ConvertAsync(string source, string stage, CancellationToken cancellationToken)
    {
        string executable = FindExecutable() ??
            throw new InvalidOperationException("Install LibreOffice to import this Office file, then retry the import.");
        string profile = Path.Combine(stage, "office-profile");
        string userProfile = Directory.CreateDirectory(Path.Combine(profile, "user")).FullName;
        string outputFolder = Directory.CreateDirectory(Path.Combine(stage, "office-output")).FullName;
        File.WriteAllText(Path.Combine(userProfile, "registrymodifications.xcu"), ProfileConfiguration);
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = stage,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { $"-env:UserInstallation={new Uri(profile).AbsoluteUri}",
                     "--headless", "--nologo", "--nodefault", "--norestore", "--convert-to", "pdf", "--outdir", outputFolder, source })
        {
            start.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConversionTimeout);
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new IOException("LibreOffice did not start.");
        var outputReading = process.StandardOutput.ReadToEndAsync();
        var errorReading = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await Task.WhenAll(outputReading, errorReading).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("LibreOffice did not finish converting the file within two minutes.");
        }

        string pdf = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(source) + DocumentProcessor.PdfExtension);
        if (process.ExitCode != 0 || !File.Exists(pdf))
            throw new InvalidDataException("LibreOffice could not open this file and export a PDF.");
        return pdf;
    }

    internal static string? FindExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (string folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                         .Select(Environment.GetFolderPath).Where(path => path.Length > 0))
            {
                string path = Path.Combine(folder, "LibreOffice", "program", "soffice.com");
                if (File.Exists(path)) return path;
            }
        }
        if (OperatingSystem.IsMacOS())
        {
            const string standardMacPath = "/Applications/LibreOffice.app/Contents/MacOS/soffice";
            if (File.Exists(standardMacPath)) return standardMacPath;
        }
        string name = OperatingSystem.IsWindows() ? "soffice.com" : "soffice";
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Path.IsPathFullyQualified(directory)) continue;
            string path = Path.Combine(directory, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
