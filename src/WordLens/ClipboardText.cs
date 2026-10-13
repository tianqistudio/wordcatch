using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace WordLens;

public sealed record ClipboardRead(string? Text = null, string? Error = null);

public static class ClipboardText
{
    // Called on the UI's STA thread only when the user requests a lookup.
    public static ClipboardRead Read() => Read(() => Clipboard.GetText(TextDataFormat.UnicodeText));

    internal static ClipboardRead Read(Func<string> read)
    {
        try
        {
            string text = read().Trim();
            if (text.Length == 0) return new();
            if (text.Length > 2000) return new(Error: "剪贴板文字超过 2,000 字符，请复制较短的内容后重试。");
            return new(text);
        }
        catch (ExternalException) { return new(Error: "剪贴板正忙，请重新按快捷键取词。"); }
    }
}
