# ADR 004 • Windows ARM64 distribution

Accepted 2026-11-18

Add Windows ARM64 to Tier 1 using the `windows-11-arm` runner. Both its ZIP and per-user setup contain the ARM64 .NET app, PDFium, and Tesseract.

The NSIS setup stub runs through Windows 11's x86 emulation. It checks the host before installing the ARM64 payload. Installed-app verification checks the runtime identifier from inside Plico, which also proves the architecture when the build shell uses an emulated runtime.

The release gate requires the ordinary CI and both package journeys for this target. Its portable and setup assets are included in the published-download checks.

[Windows 11 ARM64 runner](https://github.com/actions/partner-runner-images/blob/main/README.md) · [Windows on Arm emulation](https://learn.microsoft.com/en-us/windows/arm/apps-on-arm-x86-emulation)
