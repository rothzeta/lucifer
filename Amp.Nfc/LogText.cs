using System.Globalization;
using System.Text;

namespace Amp.Nfc;

static class LogText
{
    /// <summary>Server- or tag-supplied text for the journal: control, Unicode format (including supplementary-plane tag characters and bidi overrides) and line or paragraph separator characters become '?'.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var cleaned = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
            cleaned.Append(Replace(rune) ? "?" : rune.ToString());
        return cleaned.ToString();
    }

    private static bool Replace(Rune rune) =>
        Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
