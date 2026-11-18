using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plico.Core;

/// <summary>A state that needs attention when comparing the library catalog with the PDF files.</summary>
public enum IntegrityIssueKind { Missing, Unreadable, ChecksumMismatch, NoChecksum, Unindexed, NoSearchableText, IndexPending }

/// <summary>A relative library path and the condition found by the integrity check.</summary>
public sealed record IntegrityIssue(string RelativePath, IntegrityIssueKind Kind, bool InTrash = false);

/// <summary>The number of files checked and the issues to review without changing the library.</summary>
public sealed record IntegrityReport(int FilesChecked, IReadOnlyList<IntegrityIssue> Issues);

public sealed partial class InboxLibrary
{
    private const string MetadataExportStem = "Plico metadata";
    private const string SearchExportStem = "Plico search results";
    private const int MetadataSchemaVersion = 1;
    private static readonly JsonSerializerOptions PortableJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Compares each visible or trashed PDF with its catalog checksum and reports discrepancies.</summary>
    // O(files + catalog rows + hashed bytes) time and O(catalog rows + issues) space, with a bounded hash buffer.
    public IntegrityReport CheckIntegrity(CancellationToken cancellationToken = default)
    {
        _fileOperations.Wait(cancellationToken);
        try
        {
            var remaining = _catalog.ReadIndexedFiles();
            var issues = new List<IntegrityIssue>();
            int checkedFiles = 0;
            foreach (string path in EnumeratePdfPaths())
            {
                cancellationToken.ThrowIfCancellationRequested();
                checkedFiles++;
                string relative = RelativePdfPath(path);
                if (!remaining.Remove(relative, out var record))
                {
                    issues.Add(new(relative, IntegrityIssueKind.Unindexed));
                    continue;
                }
                var issue = CheckIntegrityFile(path, record.Fingerprint, cancellationToken);
                if (issue is { } kind) issues.Add(new(relative, kind));
                if (!record.Version.Present) issues.Add(new(relative, IntegrityIssueKind.IndexPending));
                else if (!record.HasText) issues.Add(new(relative, IntegrityIssueKind.NoSearchableText));
            }
            foreach (var record in remaining.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IntegrityIssueKind kind;
                try
                {
                    kind = ReadExistingAttributes(ResolvePdfPath(record.RelativePath)) is null
                        ? IntegrityIssueKind.Missing : IntegrityIssueKind.Unreadable;
                }
                catch (Exception error) when (IsFileError(error))
                {
                    kind = IntegrityIssueKind.Unreadable;
                }
                issues.Add(new(record.RelativePath, kind));
            }
            foreach (var file in _catalog.ReadTrashFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    string path = ResolveTrashFile(file.DocumentId);
                    if (ReadExistingAttributes(path) is not null) checkedFiles++;
                    if (CheckIntegrityFile(path, file.Fingerprint, cancellationToken) is { } kind)
                        issues.Add(new(file.OriginalPath, kind, InTrash: true));
                }
                catch (Exception error) when (IsFileError(error))
                {
                    issues.Add(new(file.OriginalPath, IntegrityIssueKind.Unreadable, InTrash: true));
                }
            }
            return new(checkedFiles, issues);
        }
        finally
        {
            _fileOperations.Release();
        }
    }

    private static IntegrityIssueKind? CheckIntegrityFile(string path, byte[]? expected, CancellationToken token)
    {
        try
        {
            if (ReadExistingAttributes(path) is not { } attributes) return IntegrityIssueKind.Missing;
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return IntegrityIssueKind.Unreadable;
            var before = FileVersion.Read(path);
            byte[] actual = FingerprintPdf(path, token);
            EnsureCurrentFile(path, before);
            if (expected is null) return IntegrityIssueKind.NoChecksum;
            return actual.AsSpan().SequenceEqual(expected) ? null : IntegrityIssueKind.ChecksumMismatch;
        }
        catch (Exception error) when (IsFileError(error))
        {
            return IntegrityIssueKind.Unreadable;
        }
    }

    /// <summary>Writes portable catalog metadata, tag rules, and saved searches as a JSON file outside the library.</summary>
    public Task<string> ExportMetadataAsync(string destination, CancellationToken cancellationToken = default) =>
        ExportReportAsync(destination, MetadataExportStem, ".json", WriteMetadataJson, cancellationToken);

    /// <summary>Writes the current search and sort as a CSV file outside the library.</summary>
    public Task<string> ExportSearchCsvAsync(DocumentFilter filter, string destination, CancellationToken cancellationToken = default) =>
        ExportReportAsync(destination, SearchExportStem, ".csv", (path, token) => WriteSearchCsv(filter, path, token), cancellationToken);

    private async Task<string> ExportReportAsync(string destination, string stem, string extension, Action<string, CancellationToken> write,
        CancellationToken token)
    {
        await _fileOperations.WaitAsync(token).ConfigureAwait(false);
        string? stage = null;
        try
        {
            string target = ResolveExternalTarget(destination);
            stage = CreateCopyStage(target, ExportStagePrefix);
            string path = Path.Combine(stage, stem + extension);
            write(path, token);
            if (Path.GetRelativePath(target, ResolveExternalTarget(target)) != ".")
                throw new IOException("The export folder changed during the copy. Choose it again.");
            return PublishExportFile(path, target, stem, extension, token);
        }
        finally
        {
            if (stage is not null) TryRemoveStage(stage);
            _fileOperations.Release();
        }
    }

    private void WriteMetadataJson(string path, CancellationToken token)
    {
        using var output = File.Open(path, FileMode.CreateNew, FileAccess.Write);
        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteNumber("schemaVersion", MetadataSchemaVersion);
        json.WriteStartArray("documents");
        foreach (var item in _catalog.ReadPortableMetadata(token)) JsonSerializer.Serialize(json, item, PortableJson);
        json.WriteEndArray();
        json.WritePropertyName("savedSearches");
        JsonSerializer.Serialize(json, _catalog.ReadSavedSearches(), PortableJson);
        json.WritePropertyName("tagRules");
        JsonSerializer.Serialize(json, _catalog.ReadFilingRules(), PortableJson);
        json.WriteEndObject();
    }

    private void WriteSearchCsv(DocumentFilter filter, string path, CancellationToken token)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)) { NewLine = "\r\n" };
        writer.WriteLine("Id,Title,Path,Added,Document date,Reviewed,Pages,Searchable text,Tags");
        _catalog.VisitSearchResults(filter, document =>
        {
            var format = CultureInfo.InvariantCulture;
            writer.WriteLine(string.Join(',', document.Id.ToString(format), CsvCell(document.Title), CsvCell(document.RelativePath),
                document.AddedAt.ToString("O", format), document.DocumentDate?.ToString("yyyy-MM-dd", format) ?? "",
                document.Reviewed.ToString(format), document.PageCount.ToString(format), document.HasText.ToString(format),
                CsvCell(string.Join(", ", document.Tags))));
        }, token);
    }

    private static string CsvCell(string value)
    {
        var significant = value.AsSpan().TrimStart();
        if ((!significant.IsEmpty && significant[0] is '=' or '+' or '-' or '@') ||
            (value.Length > 0 && value[0] is '\t' or '\r' or '\n')) value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
