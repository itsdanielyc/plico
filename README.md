# Plico

A local paperwork inbox for Windows, macOS, and Linux. Scan or import documents and find them with tags and full-text search. The library holds ordinary PDFs you can open in your usual viewer.

## Download

Get Plico from [GitHub Releases](https://github.com/itsdanielyc/plico/releases). The archives include the .NET runtime and English OCR data. Extract the entire archive before launching.

| Platform | Launch |
| --- | --- |
| Windows 11 x64 | Run the setup executable and open **Plico** from Start. For the portable ZIP, open `Plico/Plico.exe` after extraction. |
| macOS 26 on Apple silicon | Move `Plico.app` to Applications and open it |
| Ubuntu 24.04 x64 | Run `./Plico/Plico` from the extracted tar.gz |

The Windows executable and setup are unsigned. Microsoft Defender SmartScreen may show “Windows protected your PC.” The Mac app is ad-hoc signed and Gatekeeper may require a per-app approval because the build has no notarization. [Microsoft SmartScreen](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation) · [Apple Gatekeeper](https://support.apple.com/en-us/102445)

Windows setup installs for your user and adds **Plico** to the Start menu and **Installed apps**. Uninstall preserves the library folder and its contents.

On Linux, Plico uses X11 or XWayland. A minimal Ubuntu installation may need `libx11-6`, `libice6`, `libsm6`, `libfontconfig1`, and `libicu74`.

## Use Plico

1. Drop PDFs, PNGs, or JPEGs into Plico or select **Add files**. Select **Scan** to capture a new document.
2. Select a document to preview it and add tags. **Mark reviewed** takes it out of the Inbox.
3. Search by name or document text, including documents you already reviewed. Choose a tag to narrow the results.

The library starts under Documents/Plico. Select the library name in the sidebar to choose another folder. **Show PDF folder** opens the `PDFs/` directory. **Refresh index** picks up PDFs that you add, edit, or remove on disk. To move or back up a library with its tags, close Plico and copy the entire directory, including `.plico/`. Digital PDFs with text on every page are copied unchanged.

Plico recognizes exact source files already in the library and reports that it reused the existing document. Existing libraries upgrade automatically. For older entries, repeat detection can also compare an unchanged digital PDF to the file you import.

### Scanner compatibility

- Windows uses scanners with a WIA driver. Linux uses SANE drivers plus the `libsane1` runtime for supported local scanners.
- All three platforms discover compatible eSCL or AirScan network scanners on the local network. On macOS you can also import a PDF saved by Image Capture for a USB-only scanner.

Scanning supports automatic source selection, flatbed, feeder, and duplex when the selected device provides them. OCR and search run locally. Scan discovery contacts the local network when you open the scanner picker.

If your scanner software saves to a folder, choose **Watch a folder…** under **Library** and select its output folder. Plico imports new or updated PDFs and images after they settle. The watch covers files directly inside that folder and lasts while Plico is open. **Stop watching** ends intake. Your scanner's source files remain in the output folder.

## Development

Use the .NET SDK in [global.json](global.json).

```sh
dotnet run --project src/Plico.Desktop
dotnet test Plico.slnx -c Release
```

Native verification commands are in [keel.json](keel.json).
See [the product brief](PRODUCT.md), [roadmap](PLAN.md), and [verification ledger](VERIFICATION.md).
