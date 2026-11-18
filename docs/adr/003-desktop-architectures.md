# ADR 003 • Desktop architectures

Accepted 2026-11-18

The initial platform tiers in ADR 002 covered a single architecture per OS. This decision added Intel macOS and ARM64 Ubuntu to Tier 1. A later decision added Windows ARM64. [ADR 004](004-windows-arm64.md)

| Target | Hosted runner | Release bundles |
| --- | --- | --- |
| Windows x64 | windows-2025 | ZIP and Windows setup |
| macOS Apple silicon | macos-26 | App ZIP and disk image |
| macOS Intel | macos-26-intel | App ZIP and disk image |
| Ubuntu x64 | ubuntu-24.04 | tar.gz and Debian package |
| Ubuntu ARM64 | ubuntu-24.04-arm | tar.gz and Debian package |

Each architecture gets a self-contained .NET runtime and its matching PDFium and Tesseract binaries. Archive verification exercises both formats on native hardware before the release workflow publishes them.

Tier 2 and Tier 3 were empty. A new target needs an exact runtime, portable bundle, and native launch evidence. The additional macOS and Ubuntu jobs checked the same product on both hardware families. [Current tiers](../../keel.json)

[GitHub-hosted runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) · [Avalonia platform support](https://docs.avaloniaui.net/docs/supported-platforms)
