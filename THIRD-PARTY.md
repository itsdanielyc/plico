# Third-party software in Plico

Release archives carry package notices under `licenses/`, in `Contents/Resources/` on macOS. The application ships the .NET runtime, Avalonia, SQLite, and the document components below. The complete resolved package versions are recorded in the repository's NuGet lock files.

| Component | License or notices | Sources |
| --- | --- | --- |
| Avalonia and bundled Inter fonts | MIT and SIL Open Font License | [Avalonia](https://github.com/AvaloniaUI/Avalonia), [Inter](https://github.com/rsms/inter) |
| NAPS2 SDK and image modules 1.6.1 | LGPL 2.1 or later | [Corresponding SDK sources](https://github.com/cyanfish/naps2/tree/c6c762214293097c522fc1b7656f106fbb66a7f2) |
| PDFium and Tesseract binaries | See each redistributed package's license and upstream component notices | [PDFium build](https://github.com/cyanfish/naps2-pdfium/tree/2b189e0edae6c0b4f9bdd97681bde8eea8b44661), [Tesseract build](https://github.com/cyanfish/naps2-tesseract/tree/638c2e40b54eb2b30b00898e12a514448462da63) |
| English, French, and German OCR models | Apache 2.0 | [tessdata_fast 4.1.0](https://github.com/tesseract-ocr/tessdata_fast/tree/65727574dfcd264acbb0c3e07860e4e9e9b22185), `tessdata/LICENSE` |
| PdfPig | Apache 2.0 | [PdfPig](https://github.com/UglyToad/PdfPig/tree/a7bb35662bbbf405efddad50aedc9bcdcf515afc) |
| LibTiff.Net 2.4.660 | LibTiff.Net license and component notices. Copyright © 2008–2024 Bit Miracle | [Package](https://www.nuget.org/packages/BitMiracle.LibTiff.NET/2.4.660), [included notice](assets/notices/BitMiracle.LibTiff.NET-2.4.660.txt), [upstream notice](https://github.com/BitMiracle/libtiff.net/blob/cc084734b8fdceff0c32b62fd74cf3a93c384fed/license.txt) |
| SQLite | Public domain | [SQLite copyright](https://sqlite.org/copyright.html) |
| .NET runtime | MIT and component notices | [.NET Runtime](https://github.com/dotnet/runtime) |
| NSIS 3.13 Windows setup | NSIS license | [NSIS license](https://nsis.sourceforge.io/License), [compiler release](https://nsis.sourceforge.io/Download) |

The NAPS2 assemblies are separate modules. These assemblies may be replaced with API-compatible builds, including for debugging changes to the LGPL-covered modules. On macOS, a modified bundle can be re-signed with an ad-hoc signature.
