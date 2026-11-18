# Plico

Plico is a desktop paperwork inbox for people who need to collect receipts, letters, and forms and find them again by what they say. Scan or import paperwork, let Plico recognize the text on the computer, and sort it with tags. The library is a folder of searchable PDFs that can be opened, copied, and backed up with ordinary tools.

## Audience and jobs

| Situation | Job | What good looks like |
| --- | --- | --- |
| Household paperwork accumulates in scans and downloads | Collect the current work in one inbox | A paper scan or existing file appears in the same review queue |
| An independent worker needs a past receipt or contract | Retrieve it by a phrase or tag | A matching document opens quickly in the usual PDF viewer |
| A person moves their library to another computer | Keep their files and organization | Copying the library folder carries PDFs and tags together |

## Surfaces

Windows, macOS, and desktop Linux ship in the first release. The desktop app owns file import, scanner discovery, review, search, and access to the underlying folder. Its core workflow runs offline.

## Experience

New pages land in the Inbox after processing. A person can select a result, see its first page, add tags, open the PDF, and mark it reviewed. Existing digital PDFs retain their text. Scan-only pages get searchable text in the PDF itself. The search box looks through the whole library. The library folder is always accessible from the app. Progress and failures identify the affected file so a batch can continue safely.

## Success criteria

1. On each supported release platform, import a fixture with image-only text and copy out a PDF that an independent reader can search.
2. Import a text PDF, tag it, mark it reviewed, and find it by body text and tag after restarting the app.
3. Open the library in the file manager and open its PDFs with the app closed.
4. Add a multipage scan from a compatible local driver or network scanner. Exercise capture options against a real device when one is available.
5. Launch the extracted release and complete the import/search journey on clean runners for Windows, macOS, and Linux.

## First-release boundaries

- A personal library on one machine at a time. Shared editing, synchronization, and hosted access need a separate design.
- Bundled English OCR. Additional languages can follow without changing the library format.
- PDF and common document images. Office conversion needs a separate import adapter.

## Risks and decisions to validate

| Risk | First check |
| --- | --- |
| Scanner discovery depends on devices, drivers, and OS permissions | Exercise native discovery across runners and record any physical-device tests separately |
| OCR can miss handwriting and low-resolution text | Check a legible image fixture, and keep the source image in the searchable PDF |
| A folder move or interrupted import can separate PDFs and the index | Exercise interruption recovery and reopen a copied library in a different directory |
| Signing may affect first launch | Verify the exact archives on hosted runners and describe their signing status in the release |

## Assumptions

- Ruling. Plico's GitHub repository is public at the owner's request.
- Ruling. Start with Windows x64, macOS Apple silicon, and Linux x64. Additional architectures incur native-package and verification work.

## Provenance

| Inspected | Date | Applied idea | Source |
| --- | --- | --- | --- |
| Paperless-ngx setup and usage | 2026-11-18 | OCR, tags, and full-text retrieval as one workflow | [Setup](https://docs.paperless-ngx.com/setup/), [Usage](https://docs.paperless-ngx.com/usage/) |
| NAPS2 SDK scanning and OCR samples | 2026-11-18 | Scanner selection and searchable PDF output | [SDK documentation](https://www.naps2.com/sdk/doc/api/) |
| Docspell overview and metadata | 2026-11-18 | User-defined tags for personal workflows | [Metadata](https://docspell.org/docs/webapp/metadata/) |
