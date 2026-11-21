using System.Security.Cryptography;
using System.Text.Json;

namespace Plico.Core;

/// <summary>The formats accepted by the offline library migration command.</summary>
public enum LibraryMigrationFormat { Paperless, Evernote, Mariner }

/// <summary>A field or file that needs attention after a migration.</summary>
public sealed record LibraryMigrationNotice(string Record, string Field, string Message, string? OriginalValue = null);

/// <summary>The completed records and notices from a migration attempt.</summary>
public sealed record LibraryMigrationReport(int Added, int Reused, int Failed, bool Cancelled,
    IReadOnlyList<LibraryMigrationNotice> Notices);

internal sealed record MigrationEntry(MigrationSourceKey Key, ImportedDocumentMetadata Metadata,
    Func<string, CancellationToken, Task<string>> Materialize, IReadOnlyList<LibraryMigrationNotice> Notices);

public sealed partial class InboxLibrary
{
    private static readonly JsonSerializerOptions MigrationReportJson = new() { WriteIndented = true };

    /// <summary>Saves a migration report to the chosen file outside the library, atomically replacing a confirmed destination.</summary>
    public void SaveMigrationReport(string destination, LibraryMigrationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        string fullPath = Path.GetFullPath(destination);
        string filename = Path.GetFileName(fullPath);
        if (filename.Length == 0) throw new ArgumentException("Choose a file for the migration report.", nameof(destination));
        string folder = Path.GetDirectoryName(fullPath)!;
        string target = ResolveExternalTarget(folder);
        string stage = CreateCopyStage(target, ExportStagePrefix);
        try
        {
            string stagedReport = Path.Combine(stage, filename);
            using (var output = File.Open(stagedReport, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(output, report, MigrationReportJson);
            if (Path.GetRelativePath(target, ResolveExternalTarget(folder)) != ".")
                throw new IOException("The export folder changed during the copy. Choose it again.");
            File.Move(stagedReport, Path.Combine(target, filename), overwrite: true);
        }
        finally
        {
            TryRemoveStage(stage);
        }
    }

    /// <summary>Copies documents from a vendor export and preserves the source archive and any local catalog edits.</summary>
    public async Task<LibraryMigrationReport> MigrateLibraryAsync(string source, LibraryMigrationFormat format,
        CancellationToken cancellationToken = default, OcrLanguage language = OcrLanguage.English, string? destinationFolder = null)
    {
        string workspace = Directory.CreateTempSubdirectory("plico-migration-").FullName;
        var notices = new List<LibraryMigrationNotice>();
        var linksToResolve = new List<(MigrationEntry Entry, string Path)>();
        int added = 0;
        int reused = 0;
        int failed = 0;
        bool cancelled = false;
        try
        {
            foreach (var entry in ReadMigration(source, format, language, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string label = entry.Metadata.Title ?? entry.Key.RecordKey;
                string stage = Directory.CreateDirectory(Path.Combine(workspace, Path.GetRandomFileName())).FullName;
                bool retained = false;
                try
                {
                    string input = await entry.Materialize(stage, cancellationToken).ConfigureAwait(false);
                    var fields = entry.Metadata.Fields.Where(field => field.Value is not ImportedFieldValue.SourceLink).ToArray();
                    bool hasLinks = fields.Length != entry.Metadata.Fields.Count;
                    var metadata = hasLinks ? entry.Metadata with { Fields = fields } : entry.Metadata;
                    var result = await ImportMigratedFileAsync(input, entry.Key, metadata, cancellationToken, language, destinationFolder)
                        .ConfigureAwait(false);
                    if (result.Added) added++;
                    else reused++;
                    AddMigrationConflicts(notices, label, result.Conflicts);
                    if (hasLinks)
                    {
                        linksToResolve.Add((entry, input));
                        retained = true;
                    }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
                {
                    failed++;
                    notices.Add(new(label, "File", error.Message));
                }
                finally
                {
                    notices.AddRange(entry.Notices);
                    if (!retained) TryRemoveStage(stage);
                }
            }
            foreach (var (entry, input) in linksToResolve)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var links = entry.Metadata.Fields.Where(field => field.Value is ImportedFieldValue.SourceLink).ToArray();
                var linksOnly = new ImportedDocumentMetadata(null, null, null, [], [], null, null, links, null);
                var result = await ImportMigratedFileAsync(input, entry.Key, linksOnly, cancellationToken, language, destinationFolder)
                    .ConfigureAwait(false);
                string label = entry.Metadata.Title ?? entry.Key.RecordKey;
                AddMigrationConflicts(notices, label, result.Conflicts);
                foreach (var conflict in result.Conflicts)
                {
                    if (links.FirstOrDefault(field => field.Name == conflict.Field)?.Value is ImportedFieldValue.SourceLink link)
                        notices.Add(new(label, conflict.Field, "Source document link", link.Target.RecordKey));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }
        finally
        {
            TryRemoveStage(workspace);
        }
        return new(added, reused, failed, cancelled, notices);
    }

    private static IEnumerable<MigrationEntry> ReadMigration(string source, LibraryMigrationFormat format, OcrLanguage language,
        CancellationToken token) => format switch
    {
        LibraryMigrationFormat.Paperless => PaperlessMigration.Read(source, token),
        LibraryMigrationFormat.Evernote => EvernoteMigration.Read(source, language, token),
        LibraryMigrationFormat.Mariner => MarinerMigration.Read(source, token),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static void AddMigrationConflicts(List<LibraryMigrationNotice> notices, string record, IReadOnlyList<MigrationFieldConflict> conflicts)
    {
        foreach (var conflict in conflicts) notices.Add(new(record, conflict.Field, conflict.Message));
    }
}

internal static class MigrationFiles
{
    internal static string StableKey(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));

    internal static string ValidateRelativePath(string path)
    {
        string portable = path.Replace('\\', '/');
        if (portable.Length == 0 || Path.IsPathRooted(portable) || portable.Contains(':', StringComparison.Ordinal) ||
            portable.Split('/').Any(segment => segment is "" or "." or ".." || segment.Any(char.IsControl)))
        {
            throw new InvalidDataException("The export refers to a file outside its source folder.");
        }
        return portable;
    }

    internal static string? LocateFile(string directory, string relative)
    {
        string path = directory;
        foreach (string segment in ValidateRelativePath(relative).Split('/'))
        {
            path = Path.Combine(path, segment);
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The source file is linked outside the export.");
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
        }
        return File.Exists(path) ? path : null;
    }

    internal static async Task<string> StageAsync(Stream source, string stage, string name, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();
        if (!InboxLibrary.SupportsImport("input" + extension))
            throw new InvalidDataException($"The source format {extension} is unavailable for import.");
        string target = Path.Combine(stage, "document" + extension);
        await using var output = File.Open(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return target;
    }
}
