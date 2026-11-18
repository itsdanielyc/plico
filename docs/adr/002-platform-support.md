# ADR 002 • Platform support

Accepted 2026-11-18

| Keel tier | Targets | Promise |
| --- | --- | --- |
| 1 | Windows x64, macOS Apple silicon, Linux x64 | Every mainline change passes native startup and library verification on its hosted runner. Release archives pass the same workflow after extraction. |
| 2 | None | Add a target when its native OCR, scanner dependencies, and a runner can be verified on a release cadence. |
| 3 | None | Add distribution checks when a specific Linux distribution outside the runner baseline is targeted. |

Runner baseline is Windows Server 2025, macOS 26, and Ubuntu 24.04. Advertise Windows 11 x64, macOS 26 Apple silicon, and Ubuntu 24.04 x64 initially. Record claims for earlier OS versions or extra distributions after direct testing.

Each check costs one hosted job per platform. Release smoke checks run an additional three-job matrix against the assets. Physical scanner support is exercised separately from hosted GUI coverage. Initial capture targets Windows WIA, Linux SANE, and network eSCL on all three platforms.

[GitHub-hosted runner reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
