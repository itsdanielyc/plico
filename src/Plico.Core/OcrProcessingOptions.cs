using System.Runtime.InteropServices;

namespace Plico.Core;

/// <summary>The offline engine used to embed recognized text in a PDF.</summary>
public enum OcrEngineKind { Tesseract, RapidOcr }

/// <summary>Machine-local recognition options for new OCR work.</summary>
public sealed record OcrProcessingOptions(OcrEngineKind Engine = OcrEngineKind.Tesseract, bool AutoOrient = false)
{
    /// <summary>The recognition behavior used by existing libraries and older settings.</summary>
    public static OcrProcessingOptions Default { get; } = new();

    /// <summary>Whether the process architecture has a packaged RapidOCR runtime.</summary>
    public static bool RapidOcrAvailable => !(OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
}

public sealed partial class InboxLibrary
{
    /// <summary>Chooses the local settings to capture when an OCR operation begins.</summary>
    public void ConfigureOcr(OcrProcessingOptions options) => _processor.ConfigureOcr(options);
}
