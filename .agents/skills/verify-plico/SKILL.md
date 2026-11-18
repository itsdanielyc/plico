---
name: verify-plico
description: Verify Plico's library tests and native desktop journey on a release platform.
---

Start with the test filter that covers the change. Run `dotnet test Plico.slnx -c Release` from the repository root for the full suite.

Run `pwsh -File scripts/Test-Native.ps1 -Executable <path-to-Plico> -EvidenceDirectory artifacts/native` against the desktop executable. Use `xvfb-run -a` before the PowerShell command on headless Linux.

The harness uses the repository fixtures in a temporary library and scanner output folder. It records screenshots and `progress.txt`. A completed journey writes `passed.txt`, and a caught app failure writes `error.txt`. The script reports early process exits and includes the last recorded capture in timeout errors. Open the screenshots after UI changes and record the level, platform, and run in the verification ledger. [Verification strategy](../../../TESTING.md) · [Evidence](../../../VERIFICATION.md)
