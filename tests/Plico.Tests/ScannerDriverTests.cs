using System.Runtime.InteropServices;
using NAPS2.Scan;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class ScannerDriverTests
{
    [Fact]
    public void LocalDriversSupplementNetworkDiscoveryOnTheirOwnPlatforms()
    {
        Assert.Equal(new[] { Driver.Wia, Driver.Twain, Driver.Escl }, Scanners.LocalAndNetworkDrivers(OSPlatform.Windows));
        Assert.Equal(new[] { Driver.Apple, Driver.Escl }, Scanners.LocalAndNetworkDrivers(OSPlatform.OSX));
        Assert.Equal(new[] { Driver.Sane, Driver.Escl }, Scanners.LocalAndNetworkDrivers(OSPlatform.Linux));
    }

    [Fact]
    public void IdenticalPhysicalDevicesFromDifferentDriversKeepSeparateSettings()
    {
        var wia = new ScannerDevice(new ScanDevice(Driver.Wia, "sample-id", "Sample scanner"));
        var twain = new ScannerDevice(new ScanDevice(Driver.Twain, "sample-id", "Sample scanner"));
        var apple = new ScannerDevice(new ScanDevice(Driver.Apple, "sample-id", "Sample scanner"));

        Assert.NotEqual(wia.SettingsKey, twain.SettingsKey);
        Assert.NotEqual(apple.SettingsKey, twain.SettingsKey);
        Assert.EndsWith("Windows TWAIN", twain.DisplayName, StringComparison.Ordinal);
        Assert.EndsWith("macOS Image Capture", apple.DisplayName, StringComparison.Ordinal);
    }
}
