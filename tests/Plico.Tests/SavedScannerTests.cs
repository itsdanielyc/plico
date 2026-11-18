using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed class SavedScannerTests
{
    [Fact]
    public void SameAddressUpdatesItsNameAcrossSessions()
    {
        using var directory = new TemporaryLibrary();
        var store = new SavedScannerStore(directory.Path);
        store.AddOrUpdate(Scanners.FromAddress("desk-scanner.test", "Original name"));
        new SavedScannerStore(directory.Path).AddOrUpdate(
            Scanners.FromAddress("http://DESK-SCANNER.test:80/eSCL/", "Desk scanner"));

        var restored = Assert.Single(new SavedScannerStore(directory.Path).Load());
        Assert.Equal("Desk scanner", restored.Name);
        Assert.Equal(new Uri("http://desk-scanner.test/eSCL"), restored.Address);
    }

    [Fact]
    public void ForgettingOneAddressKeepsTheOther()
    {
        using var directory = new TemporaryLibrary();
        var store = new SavedScannerStore(directory.Path);
        var downstairs = Scanners.FromAddress("http://192.0.2.18:8080", "Downstairs");
        var upstairs = Scanners.FromAddress("https://scanner.test", "Upstairs");
        store.AddOrUpdate(downstairs);
        store.AddOrUpdate(upstairs);
        new SavedScannerStore(directory.Path).Remove(downstairs.Address!);

        Assert.Equal(upstairs.Address, Assert.Single(new SavedScannerStore(directory.Path).Load()).Address);
    }

    [Fact]
    public void InvalidSettingsFailWithoutReplacingUserData()
    {
        using var directory = new TemporaryLibrary();
        var path = Path.Combine(directory.Path, "scanners.json");
        const string invalidJson = "{";
        File.WriteAllText(path, invalidJson);
        var store = new SavedScannerStore(directory.Path);

        Assert.Throws<InvalidDataException>(() => store.AddOrUpdate(Scanners.FromAddress("scanner.test", "Office")));
        Assert.Equal(invalidJson, File.ReadAllText(path));
    }
}
