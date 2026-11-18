namespace Plico.Core;

/// <summary>A rectangle relative to the visible page, measured from the top left.</summary>
public readonly record struct PdfRedactionRegion(int PageNumber, double Left, double Top, double Right, double Bottom)
{
    internal bool Fits(int pageCount) => PageNumber >= 1 && PageNumber <= pageCount &&
        Left >= 0 && Left < Right && Right <= 1 && Top >= 0 && Top < Bottom && Bottom <= 1;
}

/// <summary>The visible page corner where a text stamp should appear.</summary>
public enum PdfStampPosition { TopRight, BottomRight, TopLeft, BottomLeft }

/// <summary>The literal text and position used to stamp selected pages.</summary>
public sealed record PdfStamp(string Text, PdfStampPosition Position)
{
    /// <summary>The maximum number of characters in a stamp.</summary>
    public const int MaximumLength = 48;

    internal string Validate()
    {
        if (!Enum.IsDefined(Position)) throw new ArgumentOutOfRangeException(nameof(Position));
        string text = Text?.Trim() ?? "";
        if (text.Length is 0 or > MaximumLength || text.Any(char.IsControl))
            throw new ArgumentException($"Enter 1 to {MaximumLength} printable characters for the stamp.");
        return text;
    }
}

/// <summary>Whether existing color JPEG scans can be recompressed to reduce the PDF size.</summary>
public enum PdfCompressionMode { Lossless, SmallerScans }

/// <summary>The original and trial output sizes, and the exported path when compression saved space.</summary>
public sealed record PdfCompressionResult(string? Location, long OriginalBytes, long OutputBytes);
