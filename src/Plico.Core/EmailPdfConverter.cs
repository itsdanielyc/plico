using System.Globalization;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using InternetMessage = MsgReader.Mime.Message;
using OutlookMessage = MsgReader.Outlook.Storage.Message;
using OutlookAttachment = MsgReader.Outlook.Storage.Attachment;

namespace Plico.Core;

/// <summary>Writes the email and its readable attachments into one document in message order.</summary>
internal static class EmailPdfConverter
{
    private sealed record Attachment(string Label, byte[]? Bytes);
    private sealed record Email(string? Subject, string? Sender, string? Recipients, DateTimeOffset? Sent,
        string Body, IReadOnlyList<Attachment> Attachments);

    internal static async Task WriteAsync(string source, string destination,
        Func<string, string, CancellationToken, Task> prepareAttachment, CancellationToken cancellationToken)
    {
        bool outlook = Path.GetExtension(source).Equals(ImportFormats.OutlookMessage, StringComparison.OrdinalIgnoreCase);
        var email = outlook ? ReadOutlook(source) : ReadInternetMessage(source);
        var attachmentLines = new List<string>();
        var attachmentPdfs = new List<string>();
        string stage = Path.GetDirectoryName(destination)!;
        foreach (var attachment in email.Attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string extension = Path.GetExtension(attachment.Label).ToLowerInvariant();
            if (attachment.Bytes is not { Length: > 0 } data || !DocumentProcessor.SupportsExtension(extension) ||
                ImportFormats.IsEmail(extension))
            {
                attachmentLines.Add($"{attachment.Label} (in original email)");
                continue;
            }
            try
            {
                string attachmentStage = Directory.CreateDirectory(Path.Combine(stage, Path.GetRandomFileName())).FullName;
                string input = Path.Combine(attachmentStage, "attachment" + extension);
                string pdf = Path.Combine(attachmentStage, "attachment-ready.pdf");
                await File.WriteAllBytesAsync(input, data, cancellationToken).ConfigureAwait(false);
                await prepareAttachment(input, pdf, cancellationToken).ConfigureAwait(false);
                attachmentPdfs.Add(pdf);
                attachmentLines.Add(attachment.Label);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or NotSupportedException)
            {
                attachmentLines.Add($"{attachment.Label} (in original email, conversion failed)");
            }
        }

        string body = attachmentPdfs.Count == 0 ? destination : Path.Combine(stage, "email-body.pdf");
        TextPdfWriter.Write(body, Lines(email, attachmentLines), cancellationToken);
        if (attachmentPdfs.Count == 0) return;

        using var combined = new PdfDocument();
        foreach (string path in attachmentPdfs.Prepend(body))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var part = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            combined.Pages.InsertRange(combined.PageCount, part, 0, part.PageCount, AnnotationCopyingType.DoNotCopy);
        }
        combined.Save(destination);
    }

    private static Email ReadInternetMessage(string source)
    {
        var message = InternetMessage.Load(new FileInfo(source), replaceInlineImageAttachments: false, loadAttachments: true);
        var header = message.Headers;
        return new Email(header?.Subject, header?.From?.ToString(), string.Join(", ", header?.To ?? []),
            header is { DateSent: var sent } && sent != default ? sent : null,
            PickBody(message.TextBody?.GetBodyAsText(), message.HtmlBody?.GetBodyAsText()),
            message.Attachments.Select(item => new Attachment(AttachmentLabel(item.FileName), item.Body)).ToArray());
    }

    private static Email ReadOutlook(string source)
    {
        using var message = new OutlookMessage(source);
        return new Email(message.Subject, message.GetEmailSenderRfc822Format(),
            message.GetEmailRecipientsRfc822Format(MsgReader.Outlook.RecipientType.To), message.SentOn,
            PickBody(message.BodyText, message.BodyHtml),
            message.Attachments.OfType<OutlookAttachment>()
                .Where(item => !item.IsInline)
                .Select(item => new Attachment(AttachmentLabel(item.FileName), item.Data)).ToArray());
    }

    private static string AttachmentLabel(string? fileName) => string.IsNullOrWhiteSpace(fileName) ? "Attachment" : fileName;

    private static string PickBody(string? plainText, string? html) =>
        !string.IsNullOrWhiteSpace(plainText) ? plainText : html is null ? "" : HtmlPaperText.Read(html);

    private static IEnumerable<string> Lines(Email email, IReadOnlyList<string> attachments)
    {
        if (!string.IsNullOrWhiteSpace(email.Subject)) yield return $"Subject: {email.Subject}";
        if (!string.IsNullOrWhiteSpace(email.Sender)) yield return $"From: {email.Sender}";
        if (!string.IsNullOrWhiteSpace(email.Recipients)) yield return $"To: {email.Recipients}";
        if (email.Sent is { } sent) yield return $"Sent: {sent.ToString("u", CultureInfo.InvariantCulture)}";
        yield return "";
        using var body = new StringReader(email.Body);
        while (body.ReadLine() is { } line) yield return line;
        if (attachments.Count == 0) yield break;
        yield return "";
        yield return "Attachments";
        foreach (string name in attachments) yield return name;
    }
}
