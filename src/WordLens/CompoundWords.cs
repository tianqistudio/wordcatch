using System;
using System.Text.RegularExpressions;

namespace WordLens;

internal static class CompoundWords
{
    internal static string[] Split(string text)
    {
        text = text.Trim().Trim('.', ',', '!', '?', ';', ':', '(', ')', '"', '“', '”');
        // Split explicit word boundaries, not arbitrary substrings of unknown words.
        if (text.Length > 120 || !Regex.IsMatch(text, @"^[A-Za-z]+(?:[’'\-_‐‑–—][A-Za-z]+)*$",
                RegexOptions.NonBacktracking))
            return Array.Empty<string>();
        text = Regex.Replace(text, @"([A-Z]+)([A-Z][a-z])", "$1 $2");
        text = Regex.Replace(text, @"([a-z])([A-Z])", "$1 $2");
        var parts = Regex.Split(text, @"[\-_‐‑–—\s]+");
        return parts.Length is >= 2 and <= 12 ? parts : Array.Empty<string>();
    }
}
