---
name: verify-plico
description: Verify Plico's library tests and native desktop journey on a release platform.
---

Run `dotnet test Plico.slnx -c Release` from the repository root.

Run `pwsh -File scripts/Test-Native.ps1 -Executable <path-to-Plico> -EvidenceDirectory artifacts/native` against the desktop executable. Use `xvfb-run -a` before the PowerShell command on headless Linux.

The harness exits automatically and writes a native screenshot and a pass or failure marker. Open the screenshot after UI changes and append the level, platform, and run to VERIFICATION.md.
