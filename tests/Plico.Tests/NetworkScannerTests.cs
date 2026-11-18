using System.Net;
using System.Net.Sockets;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class NetworkScannerTests
{
    private const string ScannerName = "Plico test scanner";
    private const string EsclPath = "/eSCL";
    private const string JobLocation = EsclPath + "/ScanJobs/1";
    private static readonly XNamespace ScanNamespace = "http://schemas.hp.com/imaging/escl/2011/05/03";
    private const string Capabilities = """
        <scan:ScannerCapabilities xmlns:scan="http://schemas.hp.com/imaging/escl/2011/05/03" xmlns:pwg="http://www.pwg.org/schemas/2010/12/sm">
          <pwg:Version>2.6</pwg:Version>
          <pwg:MakeAndModel>Plico test scanner</pwg:MakeAndModel>
          <scan:Platen>
            <scan:PlatenInputCaps>
              <scan:MinWidth>1</scan:MinWidth>
              <scan:MaxWidth>2550</scan:MaxWidth>
              <scan:MinHeight>1</scan:MinHeight>
              <scan:MaxHeight>3300</scan:MaxHeight>
              <scan:SettingProfiles><scan:SettingProfile>
                <scan:ColorModes><scan:ColorMode>RGB24</scan:ColorMode></scan:ColorModes>
                <scan:DocumentFormats><pwg:DocumentFormat>image/png</pwg:DocumentFormat></scan:DocumentFormats>
                <scan:SupportedResolutions><scan:DiscreteResolutions>
                  <scan:DiscreteResolution><scan:XResolution>150</scan:XResolution><scan:YResolution>150</scan:YResolution></scan:DiscreteResolution>
                  <scan:DiscreteResolution><scan:XResolution>300</scan:XResolution><scan:YResolution>300</scan:YResolution></scan:DiscreteResolution>
                  <scan:DiscreteResolution><scan:XResolution>600</scan:XResolution><scan:YResolution>600</scan:YResolution></scan:DiscreteResolution>
                </scan:DiscreteResolutions></scan:SupportedResolutions>
              </scan:SettingProfile></scan:SettingProfiles>
            </scan:PlatenInputCaps>
          </scan:Platen>
        </scan:ScannerCapabilities>
        """;

    private const string Ready = """
        <scan:ScannerStatus xmlns:scan="http://schemas.hp.com/imaging/escl/2011/05/03" xmlns:pwg="http://www.pwg.org/schemas/2010/12/sm">
          <pwg:Version>2.6</pwg:Version><pwg:State>Idle</pwg:State>
        </scan:ScannerStatus>
        """;

    private const string FailedJob = """
        <scan:ScannerStatus xmlns:scan="http://schemas.hp.com/imaging/escl/2011/05/03" xmlns:pwg="http://www.pwg.org/schemas/2010/12/sm">
          <pwg:State>Idle</pwg:State><scan:AdfState>ScannerAdfJam</scan:AdfState>
          <scan:Jobs><scan:JobInfo><pwg:JobUri>/eSCL/ScanJobs/1</pwg:JobUri><pwg:JobState>Aborted</pwg:JobState></scan:JobInfo></scan:Jobs>
        </scan:ScannerStatus>
        """;

    [Theory]
    [InlineData(ScanResolution.Quick, 150)]
    [InlineData(ScanResolution.Standard, 300)]
    [InlineData(ScanResolution.Detailed, 600)]
    public async Task ConnectsToAScannerByAddressAndArchivesItsPage(ScanResolution resolution, int expectedDpi)
    {
        var token = TestContext.Current.CancellationToken;
        byte[] page = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "Hardware Store Receipt.png"), token);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var scanner = builder.Build();
        int jobs = 0;
        int pageRequests = 0;
        var requested = new TaskCompletionSource<(int Horizontal, int Vertical)>(TaskCreationOptions.RunContinuationsAsynchronously);
        scanner.MapGet(EsclPath + "/ScannerCapabilities", () => Results.Text(Capabilities, "application/xml"));
        scanner.MapGet(EsclPath + "/ScannerStatus", () => Results.Text(Ready, "application/xml"));
        scanner.MapPost(EsclPath + "/ScanJobs", async (HttpContext request) =>
        {
            var options = await XDocument.LoadAsync(request.Request.Body, LoadOptions.None, request.RequestAborted);
            requested.TrySetResult(((int)options.Descendants(ScanNamespace + "XResolution").Single(),
                (int)options.Descendants(ScanNamespace + "YResolution").Single()));
            Interlocked.Increment(ref jobs);
            return Results.Created(JobLocation, null as object);
        });
        scanner.MapGet(JobLocation + "/NextDocument", () => Interlocked.Increment(ref pageRequests) == 1
            ? Results.Bytes(page, "image/png")
            : Results.NotFound());
        scanner.MapDelete(JobLocation, () => Results.NoContent());
        await scanner.StartAsync(token);

        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var selected = await Scanners.ConnectAsync(new Uri(scanner.Urls.Single()).Authority, token);
        var document = await library.ScanAsync(new ScanRequest(selected, ScanFeed.Flatbed, ScanPaper.Letter, resolution), token);

        Assert.Equal(ScannerName, selected.Name);
        Assert.Equal(1, jobs);
        Assert.Equal((expectedDpi, expectedDpi), await requested.Task.WaitAsync(token));
        Assert.Equal(1, document.PageCount);
        Assert.Equal(document.Id, Assert.Single(library.Find(new DocumentFilter(Query: "WRENCH470"), TestContext.Current.CancellationToken).Items).Id);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task InterruptedFeederCanSaveCompletedPagesOrDiscardThem(bool savePages, bool truncatePage)
    {
        var token = TestContext.Current.CancellationToken;
        var truncationSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scanner = await StartInterruptedFeederAsync(token, truncatePage, truncationSent);
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var device = await Scanners.ConnectAsync(new Uri(scanner.Urls.Single()).Authority, token);
        int decisions = 0;
        var scan = await library.ScanWithRecoveryAsync(new ScanRequest(device, ScanFeed.Feeder, ScanPaper.Letter),
            (interruption, _) =>
            {
                decisions++;
                Assert.Equal(2, interruption.CapturedPages);
                Assert.NotEmpty(interruption.Reason);
                Assert.Empty(Directory.EnumerateFiles(library.PdfDirectory, "*.pdf"));
                Assert.Empty(library.Find(new DocumentFilter(), token).Items);
                return Task.FromResult(savePages);
            }, token);

        Assert.Equal(1, decisions);
        if (truncatePage) Assert.True(truncationSent.Task.IsCompletedSuccessfully);
        Assert.Equal(2, scan.Interruption?.CapturedPages);
        if (savePages)
        {
            var partial = Assert.IsType<DocumentSummary>(scan.Document);
            Assert.EndsWith("(partial)", partial.Title);
            Assert.Equal(2, partial.PageCount);
            Assert.Equal(1, library.FindFirstMatchingPage(partial, "WRENCH470", token));
            Assert.Equal(partial.Id, Assert.Single(library.Find(new DocumentFilter(Query: "WRENCH470"), token).Items).Id);
        }
        else
        {
            Assert.Null(scan.Document);
            Assert.Empty(Directory.EnumerateFiles(library.PdfDirectory, "*.pdf"));
            Assert.Empty(library.Find(new DocumentFilter(), token).Items);
        }
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(temporary.Path, ".plico"), "import-*"));
    }

    internal static async Task<WebApplication> StartInterruptedFeederAsync(CancellationToken cancellationToken, bool truncatePage = false,
        TaskCompletionSource? truncationSent = null)
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        byte[] receipt = await File.ReadAllBytesAsync(Path.Combine(fixtures, "Hardware Store Receipt.png"), cancellationToken);
        byte[] invoice = await File.ReadAllBytesAsync(Path.Combine(fixtures, "Spanish Receipt.png"), cancellationToken);
        byte[][] pages = [receipt, invoice];
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var scanner = builder.Build();
        int pageRequests = 0;
        string feederCapabilities = Capabilities.Replace("PlatenInputCaps", "AdfSimplexInputCaps", StringComparison.Ordinal)
            .Replace("Platen", "Adf", StringComparison.Ordinal);
        scanner.MapGet(EsclPath + "/ScannerCapabilities", () => Results.Text(feederCapabilities, "application/xml"));
        scanner.MapGet(EsclPath + "/ScannerStatus", () => Results.Text(Volatile.Read(ref pageRequests) > pages.Length ? FailedJob : Ready, "application/xml"));
        scanner.MapPost(EsclPath + "/ScanJobs", () => Results.Created(JobLocation, null as object));
        scanner.MapGet(JobLocation + "/NextDocument", async context =>
        {
            int pageIndex = Interlocked.Increment(ref pageRequests) - 1;
            if (pageIndex < pages.Length)
            {
                await Results.Bytes(pages[pageIndex], "image/png").ExecuteAsync(context);
                return;
            }
            if (!truncatePage)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return;
            }
            context.Response.ContentType = "image/png";
            context.Response.ContentLength = receipt.Length;
            context.Response.Headers.Connection = "close";
            await context.Response.Body.WriteAsync(receipt.AsMemory(0, receipt.Length / 2), context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            // A write-side EOF lets the client detect the short body as soon as it reads the connection.
            // Aborting the request can leave the peer waiting for the SDK's full read timeout on Intel macOS.
            var socket = context.Features.Get<IConnectionSocketFeature>()?.Socket
                ?? throw new InvalidOperationException("The loopback scanner fixture requires a direct TCP connection.");
            socket.Shutdown(SocketShutdown.Send);
            truncationSent?.TrySetResult();
        });
        scanner.MapDelete(JobLocation, () => Results.NoContent());
        try
        {
            await scanner.StartAsync(cancellationToken);
            return scanner;
        }
        catch
        {
            await scanner.DisposeAsync();
            throw;
        }
    }

    [Theory]
    [InlineData("file:///example")]
    [InlineData("http://user:password@127.0.0.1:1")]
    [InlineData("http://127.0.0.1:1/?option=1")]
    [InlineData("")]
    public async Task RejectsAnInvalidScannerAddress(string address) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Scanners.ConnectAsync(address, TestContext.Current.CancellationToken));
}
