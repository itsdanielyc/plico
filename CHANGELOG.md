# Changelog

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
