# Changelog

## [0.8.0] - 2026-11-18

- Apply a tag to several selected documents at once. Existing spellings of matching tags are reused.
- The selection stays available after tagging so you can apply another tag or mark the documents reviewed.

## [0.7.0] - 2026-11-18

- Added Intel macOS downloads and ARM64 Ubuntu downloads.
- Intel Macs receive an app ZIP and a disk image. ARM64 Ubuntu receives a tar.gz and a Debian package.
- Both architectures run the same native paperwork and installed-package checks as the existing targets.

## [0.6.0] - 2026-11-18

- Select several documents and mark them reviewed together. A selection of reviewed documents can return to the inbox in one action.
- **Select shown** selects the current list. A watched import keeps the selection available until you finish the batch.

## [0.5.0] - 2026-11-18

- **Refresh index** can follow an indexed PDF into a new folder or filename. When the match is unique, it carries over that PDF's title, tags, review state, and duplicate detection.
- Refresh builds the required fingerprints for older libraries. New imports receive them automatically.
- If multiple copies make a match ambiguous, refresh creates separate catalog entries for the new paths.

## [0.4.1] - 2026-11-18

- Select a PDF's filename in the preview to open its containing folder. This works for documents you organize into subfolders.

## [0.4.0] - 2026-11-18

- Open the macOS disk image and drag Plico into Applications.
- Install the Ubuntu package and find Plico in the desktop menu. Package removal preserves the library.
- The release includes Windows setup, macOS disk image, and Ubuntu package downloads alongside the portable archives.

## [0.3.2] - 2026-11-18

- Edit a document's title from its preview. Search recognizes the edited title, PDF filename, and document text.
- Catalog titles, tags, and review state survive reindexing the PDF.

## [0.3.1] - 2026-11-18

- Plico remembers the addresses of scanners you connect to and offers them the next time you open Scan.
- Use **Forget** to remove a saved address from this computer.
- A settings error leaves a newly connected scanner available for the current scan.

## [0.3.0] - 2026-11-18

- Connect an eSCL scanner by hostname, IP address, or its full scanner URL from the Scan dialog.
- The dialog reads the scanner's model before enabling capture and keeps connected scanners available when you refresh discovery.

Downloads include Windows setup and ZIP, a macOS app ZIP, and a Linux tar.gz. See the README for platform and first-launch details.

## [0.2.1] - 2026-11-18

- Added a per-user Windows setup executable with a Start menu shortcut and an Installed apps entry.
- Uninstall removes the application files and preserves the PDF library.
- Portable downloads include a Windows ZIP, macOS app ZIP, and Linux tar.gz.

The Windows setup and executable are unsigned. The macOS app is ad-hoc signed. See the README for first-launch and scanner setup.

## [0.2.0] - 2026-11-18

- Watch a scanner output folder while Plico is open. New and updated PDFs, PNGs, and JPEGs enter the same OCR and duplicate detection workflow.
- Stop watching from the library sidebar. Automatic imports preserve the current search and the original files.
- File import checks that a source stays unchanged while it is copied.

Self-contained archives are available for Windows 11 x64, macOS 26 Apple silicon, and Ubuntu 24.04 x64. The Windows executable is unsigned. The macOS app is ad-hoc signed. See the README for first-launch and scanner setup.

## [0.1.1] - 2026-11-18

- Drop PDFs and images directly into the Plico window.
- Exact repeat imports reuse the existing PDF and preserve its tags and review state, including byte-identical digital PDFs from v0.1.0 libraries.
- Search excerpts put the matching text closer to the beginning of each result.
- New imports keep their own metadata when a different document previously used the same filename.

Self-contained archives are available for Windows 11 x64, macOS 26 Apple silicon, and Ubuntu 24.04 x64. The Windows executable is unsigned. The macOS app is ad-hoc signed. See the README for first-launch and scanner setup.

## [0.1.0] - 2026-11-18

- Import PDFs, PNGs, and JPEGs. Bundled English OCR embeds searchable text in image-only pages. Imports preserve the supplied files.
- Scan with Windows WIA, Linux SANE, or a local network eSCL scanner on any supported platform. One capture job produces one PDF.
- Preview each page, tag documents, mark them reviewed, and search document names and recognized text across the library.
- Keep PDFs in an ordinary folder and open them in the OS PDF viewer. Copy the whole library to carry its catalog and tags to another computer.

### Downloads

Self-contained archives are available for Windows 11 x64, macOS 26 Apple silicon, and Ubuntu 24.04 x64. Extract the archive and run Plico. See the repository README for first-run and scanner setup.

The Windows executable is unsigned and SmartScreen may display “Windows protected your PC.” The macOS app is ad-hoc signed and has no Apple notarization. Physical scanner interoperability remains unverified. [Microsoft SmartScreen](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation) · [Apple Gatekeeper](https://support.apple.com/en-us/102445)
