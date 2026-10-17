namespace Amp.Nfc;

static class LogText
{
    /// <summary>Server- or tag-supplied text for the journal: control characters (including ESC and line breaks) become '?'.</summary>
    public static string Clean(string? text) =>
        text is null ? "" : string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = char.IsControl(source[i]) ? '?' : source[i];
        });
}
