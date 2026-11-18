# macOS scanner bridge

**Status** Accepted for the capture branch

The portable NAPS2 package compiles its Apple scanner driver out. Plico builds a small Swift executable against Image Capture on each macOS architecture. It sends device IDs and completed page paths to Core. The same Core capture and publication pipeline handles those pages, OCR, and recovery after a driver error. The helper writes only into the capture stage and exits after discovery or a scan.

The Mac build requires Xcode's command-line tools. Plico packages and signs the resulting helper with the app. The scanner picker and library need no macOS-specific .NET workload.
