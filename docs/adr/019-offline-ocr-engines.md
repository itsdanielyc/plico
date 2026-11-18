# Offline OCR engines

**Status** Accepted for the capture branch

Plico keeps Tesseract as the default and bundles its orientation model. Optional orientation applies a quarter-turn to the visible page before the recognition engine runs. It requires the model's confidence threshold so uncertain samples keep their captured rotation.

RapidOCR uses the packaged Latin PP-OCRv5 models and ONNX Runtime. The adapter supplies recognized word bounding boxes to the existing NAPS2 PDF exporter. Intel Macs use Tesseract because the bundled ONNX Runtime release has no Intel Mac native library.

An OCR copy keeps the old catalog key when it uses default settings. A copy with another engine or orientation preference uses a distinct key. Both record the real language independently for filing and export.
