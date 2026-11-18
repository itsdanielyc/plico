# Verification ledger

| Date | Level | Claim | Platforms | Evidence |
| --- | --- | --- | --- | --- |
| 2026-11-18 | Scaffold · L0, L4, L6 | Hand-assembled Avalonia profile builds, opens its inbox, passes view geometry assertions, and captures the native view | windows-2025, macos-26 arm64, ubuntu-24.04 x64 | [CI 44214487818](https://github.com/itsdanielyc/plico/actions/runs/44214487818), 3 of 3 green |
| 2026-11-18 | L0, L1, L3, L4, L6 | Digital PDF preservation, mixed-page OCR, malformed-file recovery, tags and reviewed state after a move, native import and OCR-text search. With OCR disabled, both image-text tests failed because `WRENCH470` was absent. | windows-2025, macos-26 arm64, ubuntu-24.04 x64 | [CI 44215231067](https://github.com/itsdanielyc/plico/actions/runs/44215231067), 3 of 3 green, screenshots reviewed |
| 2026-11-18 | L7 | First self-contained archives extract and complete the native OCR journey at commit `9103a34` | windows-2025, macos-26 arm64, ubuntu-24.04 x64 | [Archives 44215272646](https://github.com/itsdanielyc/plico/actions/runs/44215272646), 3 of 3 green |
| 2026-11-18 | L0–L1, L3–L4, L6 | Nine tests per platform. Synthetic scanner capture produces one searchable multipage PDF; scanner picker disables capture without a device. Negative checks observed one page instead of two and capture incorrectly enabled before restoring the implementation. | windows-2025, macos-26 arm64, ubuntu-24.04 x64 | [CI 44215773716](https://github.com/itsdanielyc/plico/actions/runs/44215773716), 3 of 3 green |
| 2026-11-18 | L7 | Release candidate `09b1e1e` extracts, launches, imports an image, previews its searchable PDF, applies a tag, marks it reviewed, and retrieves it by OCR text. macOS bundle signature verified. Screenshots reviewed. | windows-2025, macos-26 arm64, ubuntu-24.04 x64 | [Archives 44215774059](https://github.com/itsdanielyc/plico/actions/runs/44215774059), 3 of 3 green |

## Unverified

- Physical scanner interoperability requires a compatible device.
- Developer ID signing and notarization on macOS, and Authenticode signing on Windows.
