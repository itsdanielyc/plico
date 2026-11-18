using System.Text.Json;

namespace Plico.Desktop;

/// <summary>Stores each library’s unresolved imports in the local user’s settings.</summary>
internal sealed class ImportFailureStore(string directory)
{
    private static readonly JsonSerializerOptions Serialization = new()
    {
        RespectRequiredConstructorParameters = true,
        IgnoreReadOnlyProperties = true
    };
    private readonly string _directory = Path.GetFullPath(directory);

    // O(n) time and space in the saved entries and their text.
    internal IReadOnlyList<ImportFailure> Load(string libraryRoot)
    {
        try
        {
            using var file = File.OpenRead(PathFor(libraryRoot));
            var failures = JsonSerializer.Deserialize<ImportFailure[]>(file, Serialization);
            if (failures is null || failures.Any(failure => !IsValid(failure)))
            {
                throw new JsonException("The saved import errors contain an invalid source, reason, or origin.");
            }
            return failures;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
    }

    internal void Save(string libraryRoot, IReadOnlyList<ImportFailure> failures) =>
        ApplicationSettings.WriteAtomically(PathFor(libraryRoot), JsonSerializer.Serialize(failures, Serialization));

    private static bool IsValid(ImportFailure? failure) =>
        failure is { SourcePath: { } path, Reason: not null, Origin: ImportOrigin.Manual or ImportOrigin.FolderWatch } &&
        !path.Contains('\0') && Path.IsPathFullyQualified(path);

    private string PathFor(string libraryRoot) => Path.Combine(_directory, $"import-errors-{ApplicationSettings.LibraryKey(libraryRoot)}.json");
}
