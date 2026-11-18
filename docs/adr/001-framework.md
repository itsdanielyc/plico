# ADR 001 • Desktop framework

Accepted 2026-11-18

## Decision

Use Avalonia with .NET 10 LTS. The desktop-only surface and cross-platform scanner/PDF SDK settled the Keel choice in step 1. Avalonia lets the UI, file processing, and search share typed C# contracts in one desktop process. Use the NAPS2 SDK for capture and searchable PDFs, and SQLite FTS5 for local indexing.

Tauri would need a bridge to a .NET scanner worker or separate native integrations. Electron adds a browser runtime and the same integration seam. Avalonia ships native windows and file dialogs on each target, with an X11 path on Linux.

## Verification and packaging

Keel profile `avalonia-desktop` is assembled by hand. C# and xUnit cover the library contract and real I/O. Avalonia Headless drives the view with a labelled fixture adapter. Native shell verification uses the real app and real storage, with a fixture path supplied in place of the file picker. The L7 matrix extracts and exercises the exact self-contained archives.

- `windows-2025` exercises the native Win32 shell.
- `macos-26` exercises the native AppKit shell.
- `ubuntu-24.04` exercises the X11 shell under Xvfb.

Ship a Windows ZIP, an Apple silicon ZIP containing Plico.app, and a Linux tar.gz. All include their runtime and English OCR data. Windows builds are unsigned. macOS builds use an ad-hoc signature and require Developer ID signing and notarization for unrestricted distribution.

[Avalonia platform support](https://docs.avaloniaui.net/docs/supported-platforms) · [Avalonia Headless](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform) · [NAPS2 SDK](https://www.naps2.com/sdk/doc/api/)
