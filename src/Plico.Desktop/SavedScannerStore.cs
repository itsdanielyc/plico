using System.Text.Json;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>Preserves validated scanner addresses across desktop sessions.</summary>
internal sealed class SavedScannerStore(string directory)
{
    private sealed record SavedAddress(string Address, string Name);
    private readonly string _directory = Path.GetFullPath(directory);
    private string AddressFile => Path.Combine(_directory, "scanners.json");

    internal IReadOnlyList<ScannerDevice> Load() => ReadByAddress().Values.ToArray();

    internal void AddOrUpdate(ScannerDevice scanner)
    {
        var address = scanner.Address ?? throw new ArgumentException("Only scanners connected by address can be saved.", nameof(scanner));
        Update(entries => entries[address] = scanner);
    }

    internal void Remove(Uri address) => Update(entries => entries.Remove(address));

    private Dictionary<Uri, ScannerDevice> ReadByAddress()
    {
        try
        {
            using var file = File.Open(AddressFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var entries = JsonSerializer.Deserialize<SavedAddress[]>(file) ??
                throw new InvalidDataException("Saved scanner addresses are unreadable.");
            var result = new Dictionary<Uri, ScannerDevice>();
            foreach (var entry in entries)
            {
                if (entry?.Address is null) throw new InvalidDataException("A saved scanner address is missing.");
                var scanner = Scanners.FromAddress(entry.Address, entry.Name);
                result[scanner.Address!] = scanner;
            }
            return result;
        }
        catch (IOException error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new InvalidDataException("Saved scanner addresses are unreadable.", error);
        }
    }

    private void Update(Action<Dictionary<Uri, ScannerDevice>> change)
    {
        Directory.CreateDirectory(_directory);
        using var lease = File.Open(Path.Combine(_directory, "scanners.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var entries = ReadByAddress();
        change(entries);
        var saved = entries.Select(entry => new SavedAddress(entry.Key.AbsoluteUri, entry.Value.Name));
        ApplicationSettings.WriteAtomically(AddressFile, JsonSerializer.Serialize(saved));
    }
}
