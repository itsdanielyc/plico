using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyAppleScannerDiscoveryAsync()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var discovery = await Scanners.DiscoverAsync(timeout.Token);
        Require(!discovery.Warnings.Any(warning => warning.StartsWith("macOS Image Capture", StringComparison.Ordinal)),
            string.Join(" ", discovery.Warnings));
    }
}
