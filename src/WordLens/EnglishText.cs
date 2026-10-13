using System.Linq;
using System.Text.RegularExpressions;

namespace WordLens;
internal static class EnglishText
{
    internal static string Extract(string text)
    {
        return string.Join("\n", Regex.Split(text, @"[\r\n]+")
            .SelectMany(line => Regex.Matches(line, @"(?<![A-Za-z\u00C0-\u024F\p{M}])[A-Za-z]+(?:[’'_-][A-Za-z]+)*(?:[ \t]+[A-Za-z]+(?:[’'_-][A-Za-z]+)*)*[.!?]?(?![A-Za-z\u00C0-\u024F\p{M}])")
                .Select(m => m.Value.Trim())));
    }
}
