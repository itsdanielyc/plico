# Offline OCR engines

**Status** Accepted for the capture branch

Plico keeps Tesseract as the default and bundles its orientation model. Optional orientation applies a quarter-turn to the visible page before the recognition engine runs. The packaged Linux and macOS executables compile Tesseract's orientation mode out. Those builds score text confidence over four bounded raster samples with the selected language model. Both paths keep the captured rotation when the result is uncertain.

RapidOCR uses the packaged Latin PP-OCRv5 models and ONNX Runtime. The adapter supplies recognized word bounding boxes to the existing NAPS2 PDF exporter. Intel Macs use Tesseract because the bundled ONNX Runtime release has no Intel Mac native library.

An OCR copy keeps the old catalog key when it uses default settings. A copy with another engine or orientation preference uses a distinct key. Both record the real language independently for filing and export.
