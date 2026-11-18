using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
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
        Assert.Equal(document.Id, Assert.Single(library.Find(new DocumentFilter(Query: "WRENCH470")).Items).Id);
    }

    [Theory]
    [InlineData("file:///example")]
    [InlineData("http://user:password@127.0.0.1:1")]
    [InlineData("http://127.0.0.1:1/?option=1")]
    [InlineData("")]
    public async Task RejectsAnInvalidScannerAddress(string address) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Scanners.ConnectAsync(address, TestContext.Current.CancellationToken));
}
