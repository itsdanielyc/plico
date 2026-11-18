# Plico

A local paperwork inbox for Windows, macOS, and Linux. Scan or import documents and find them with tags and full-text search. The library holds ordinary PDFs you can open in your usual viewer.

## Download

Get Plico from [GitHub Releases](https://github.com/itsdanielyc/plico/releases). The archives include the .NET runtime and English, French, and German OCR models. Extract the entire archive before launching.

| Platform | Launch |
| --- | --- |
| Windows 11, x64 or ARM64 | Run the setup for your architecture and open **Plico** from Start. For the portable ZIP, open `Plico/Plico.exe` after extraction. |
| macOS 26, Apple silicon or Intel | Open the disk image and drag **Plico** into Applications. The ZIP contains the app for direct extraction. |
| Ubuntu 24.04, x64 or ARM64 | Install the `.deb` with your package manager and open **Plico** from the desktop menu. The tar.gz opens with `./Plico/Plico` after extraction. |

The Windows executable and setup are unsigned. Microsoft Defender SmartScreen may show “Windows protected your PC.” The Mac app is ad-hoc signed and Gatekeeper may require a per-app approval because the build has no notarization. [Microsoft SmartScreen](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation) · [Apple Gatekeeper](https://support.apple.com/en-us/102445)

Windows setup installs for your user and adds **Plico** to the Start menu and **Installed apps**. Uninstall preserves the library folder and its contents.

On Linux, Plico uses X11 or XWayland. The Debian package declares its runtime libraries. A minimal Ubuntu installation using the portable tar.gz may need `libx11-6`, `libice6`, `libsm6`, `libfontconfig1`, and `libicu74`. [.NET on Ubuntu](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install)

## Use Plico

1. Drop PDFs, PNGs, or JPEGs into Plico or select **Add files**. Select **Scan** to capture a new document.
2. Select a document to preview it and add tags. **Mark reviewed** takes it out of the Inbox.
3. Search by name or document text, including documents you already reviewed. Choose a tag to narrow the results.

Select a language in **OCR** for scans, imports, and searchable copies. Plico remembers the choice and uses that bundled model for recognition.

When an import fails, **Import errors** lists the source files and reasons. Fix the files and choose **Retry files** to process those inputs again.

Put a phrase in quotation marks to search its adjacent whole words. For example, "lease renewal" north searches for the phrase together with a word beginning with north.

Use **Sort** to browse by newest added or title A–Z. **Automatic** puts the best matches first during a text search and the most recent documents first while browsing.

Use Ctrl or Shift when selecting rows, or choose **Select shown** to select the loaded results. Use **Tag selected** to apply a tag to the whole selection. The batch control can mark the selection reviewed or return reviewed documents to the inbox.

Use **Edit title** in the preview to change the document's catalog title. Search covers both the title and filename. Select the PDF's filename to open its containing folder.

Choose **Fit width** in the preview to enlarge the page and scroll through it. Choose **Fit page** to see it all at once.

Choose **Go to match** on a multi-page text result to open the first page that matches the full search. Terms on separate pages can match the document as a whole.

Choose **Copy text** to copy the searchable text from all pages of the selected PDF.

The library starts under Documents/Plico. Select the library name in the sidebar to choose another folder. **Show PDF folder** opens the `PDFs/` directory. **Refresh index** picks up PDFs that you add, edit, or remove on disk. To move or back up a library with its tags, close Plico and copy the entire directory, including `.plico/`. Digital PDFs with text on every page are copied unchanged.

When a scan copied into `PDFs/` needs OCR, choose **Create searchable copy** in its preview. Plico adds a searchable PDF to the inbox with the scan’s tags.

To make a copy while Plico is open, choose **Back up library…** and select a destination outside the current library. Plico creates a dated folder with the PDFs and their catalog. **Open backup** shows that folder in the file manager. Choose the folder as a library to reopen it in Plico.

Refresh also follows a uniquely identifiable PDF that you move or rename inside `PDFs/`. Its title, tags, and review state travel with its catalog record. For libraries from older Plico versions, run **Refresh index** once while the original paths are present to prepare them for moves.

Plico recognizes exact source files already in the library and reports that it reused the existing document. Existing libraries upgrade automatically. For older entries, repeat detection can also compare an unchanged digital PDF to the file you import.

### Scanner compatibility

- Windows uses scanners with a WIA driver. Linux uses SANE drivers plus the `libsane1` runtime for supported local scanners.
- All three platforms discover compatible eSCL or AirScan network scanners on the local network. On macOS you can also import a PDF saved by Image Capture for a USB-only scanner.

If discovery misses an eSCL scanner, enter its hostname, IP address, or full eSCL URL in **Scan** and select **Connect**. A hostname or IP address uses HTTP and the standard `/eSCL` path. Enter an HTTPS URL if your scanner uses HTTPS.

Plico remembers connected addresses in settings on this computer. Select a saved device in **Scan** to use it again, or select **Forget** to remove its saved address.

Scanning supports automatic source selection, flatbed, feeder, and duplex when the selected device provides them. Choose 150, 300, or 600 dpi. An eSCL scanner uses its nearest supported resolution when needed. OCR and search run locally. Scan discovery contacts the local network when you open the scanner picker.

The scan picker remembers the paper source, size, and resolution you choose.

If your scanner software saves to a folder, choose **Watch a folder…** under **Library** and select its output folder. Plico imports new or updated PDFs and images after they settle. The watch covers files directly inside that folder and lasts while Plico is open. **Stop watching** ends intake. Your scanner's source files remain in the output folder.

Plico remembers the chosen folder for the current library. **Resume folder watch** starts watching it again when you are ready.

Choose **Import existing files** while a watch is active to pick up files already in that folder, including scans saved while Plico was closed.

## Development

Use the .NET SDK in [global.json](global.json).

```sh
dotnet run --project src/Plico.Desktop
dotnet test Plico.slnx -c Release
```

Native verification commands are in [keel.json](keel.json).
See [the product brief](PRODUCT.md), [roadmap](PLAN.md), and [verification ledger](VERIFICATION.md).
