using System.Globalization;

namespace Amp.Nfc;

static class LogText
{
    /// <summary>Server- or tag-supplied text for the journal: control and Unicode format characters (including ESC, line breaks and bidi overrides) become '?'.</summary>
    public static string Clean(string? text) =>
        text is null ? "" : string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = char.IsControl(source[i]) || char.GetUnicodeCategory(source[i]) == UnicodeCategory.Format ? '?' : source[i];
        });
}
