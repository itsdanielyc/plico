using System.Text;
using HtmlAgilityPack;

namespace Plico.Core;

/// <summary>Extracts the visible wording of local HTML without loading any referenced resources.</summary>
internal static class HtmlPaperText
{
    private static readonly HashSet<string> HiddenElements = new(StringComparer.OrdinalIgnoreCase)
        { "head", "script", "style", "template", "noscript" };
    private static readonly HashSet<string> LineElements = new(StringComparer.OrdinalIgnoreCase)
        { "p", "div", "br", "tr", "li", "blockquote", "pre", "h1", "h2", "h3", "h4", "h5", "h6" };

    internal static string Read(string html)
    {
        var document = new HtmlDocument { OptionMaxNestedChildNodes = 256 };
        document.LoadHtml(html);
        var text = new StringBuilder();
        AppendVisibleText(document.DocumentNode, text);
        return text.ToString().Trim();
    }

    private static void AppendVisibleText(HtmlNode node, StringBuilder text)
    {
        if (node.NodeType == HtmlNodeType.Comment || HiddenElements.Contains(node.Name)) return;
        if (node is HtmlTextNode words)
        {
            text.Append(HtmlEntity.DeEntitize(words.Text));
            return;
        }
        if (LineElements.Contains(node.Name)) EndLine(text);
        foreach (var child in node.ChildNodes) AppendVisibleText(child, text);
        if (LineElements.Contains(node.Name)) EndLine(text);
        if (node.Name is "td" or "th") text.Append('\t');
    }

    private static void EndLine(StringBuilder text)
    {
        if (text.Length > 0 && text[^1] != '\n') text.AppendLine();
    }
}
