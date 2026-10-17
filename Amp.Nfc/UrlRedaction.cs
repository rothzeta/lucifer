using System.Text.RegularExpressions;

namespace Amp.Nfc;

/// <summary>Stream URLs carry the Subsonic user, token and salt; text that may echo one is scrubbed before it is shown.</summary>
static partial class UrlRedaction
{
    public static string Redact(string text) => Url().Replace(text, "<url>");

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+.-]*://\S+")]
    private static partial Regex Url();
}
