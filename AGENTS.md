# Plico engineering guide

<!-- keel:start -->
Plico is a local paperwork inbox for Windows, macOS, and Linux. Documents are ordinary searchable PDFs in the user's library. Read PRODUCT.md and ARCHITECTURE.md before changing that contract.

- Read `keel.json` for commands and platforms. Run the smallest relevant test first and the native journey for user-facing changes.
- Keep nullable reference analysis enabled. Use descriptive names, small typed APIs, and doc comments for public contracts. Record time and space bounds for complex algorithms.
- Keep files and SQLite consistent across retries and interruptions. Protect existing files and test failure paths with synthetic fixtures.
- Keep filesystem, OCR, and scanner code inside Core. Keep platform dialogs and rendering inside Desktop.
- Add evidence to VERIFICATION.md with the level, platform, and run. Preserve historical rows. A release tag requires green archive verification for its exact commit.
- Update PLAN.md when a slice ships and write a short ADR for a lasting architecture decision.
<!-- keel:end -->
