# Plico verification strategy

| Level | Harness | Evidence |
| --- | --- | --- |
| L0 | Release build with nullable analysis and warnings as errors | Compilation on each supported runner |
| L1 | xUnit | Literal search input, names, tags, and invalid data |
| L2/L3 | xUnit and temporary disk libraries | Import and catalog contract, FTS query, persistence and recovery |
| L4/L5 | Avalonia Headless and native screenshots | Input and selection behaviour, control presence and usable geometry, review screenshots |
| L6 | Native window plus a fixture picker | App launches, performs import, searches persisted OCR and changes review state |
| L7 | Extract the exact release archive and run the native journey | Portable install, launch, and cleanup on each Tier 1 target |

## Critical journeys

1. Import a digital PDF with selectable text. The PDF stays intact and a body term finds it after reopening the library (L3).
2. Import an image-only document. The resulting PDF contains independently extractable OCR text (L3 and L6).
3. Tag, filter, and mark a new document reviewed through the desktop view (L4 and L6).
4. Start the published archive on fresh Windows, macOS, and Ubuntu runners (L7).
5. Discover scanners from the real shell. Physical feed, duplex and flatbed checks require a compatible device (manual).
6. Start watching a folder from the native window. Import a newly completed file, find it by text, preserve the previous search until the user changes it, and stop the watch (L3 and L6).

Run `dotnet test Plico.slnx -c Release` for the fast suite. Run the native harness from `keel.json` for installed-platform evidence. Visual review starts with per-OS captures and geometry checks. Add pixel baselines when repeatable runner captures justify the maintenance.

Budget three minutes for the local suite and 20 minutes for the normal matrix. Apply the Keel flaky-test policy and record evidence in VERIFICATION.md.
