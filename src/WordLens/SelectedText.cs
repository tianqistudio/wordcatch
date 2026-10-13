using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace WordLens;
public static class SelectedText
{
    private static Task<string?>? pending;
    private static readonly object sync = new();
    internal static string LastStatus { get; private set; } = "not-run";
    public static async Task<string?> ReadAsync()
    {
        var foreground = Native.GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out uint pid);
        if (pid == Environment.ProcessId) { LastStatus = "own-window"; return null; }
        return await ReadFromWindowAsync(foreground);
    }
    internal static async Task<string?> ReadFromWindowAsync(IntPtr foreground, bool requireForeground = true)
    {
        Task<string?> task;
        lock (sync)
        {
            // An unresponsive third-party UIA provider must not accumulate blocked threads.
            if (pending != null && !pending.IsCompleted) { LastStatus = "provider-busy"; return null; }
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = task = completion.Task;
            var thread = new Thread(() =>
            {
                try { completion.TrySetResult(ReadWindowSelection(foreground)); }
                catch (Exception ex) { LastStatus = "provider-" + ex.GetType().Name; completion.TrySetResult(null); }
            }) { IsBackground = true, Name = "WordLens.Selection" };
            thread.SetApartmentState(ApartmentState.MTA); thread.Start();
        }
        try
        {
            var text = await task.WaitAsync(TimeSpan.FromMilliseconds(450));
            if (requireForeground && Native.GetForegroundWindow() != foreground) { LastStatus = "foreground-changed"; return null; }
            return text;
        }
        catch (TimeoutException) { LastStatus = "timeout"; return null; }
    }
    private static string? ReadWindowSelection(IntPtr foreground)
    {
        var root = AutomationElement.FromHandle(foreground);
        var candidates = new System.Collections.Generic.List<AutomationElement>();
        var focus = AutomationElement.FocusedElement;
        var current = focus;
        bool belongs = false;
        for (int i=0; i<16 && current!=null; i++, current=TreeWalker.ControlViewWalker.GetParent(current))
            if (current.Equals(root)) { belongs=true; break; }
        if (belongs && focus!=null)
        {
            var text = ReadElementSelection(focus);
            if (text != null) return text;
        }
        var rootText = ReadElementSelection(root);
        if (rootText != null) return rootText;
        // Some apps expose selection on the document, not on the focused child.
        // Restrict the search to the foreground window instead of assuming all
        // UIA providers share its process ID (multi-process browsers do not).
        var descendants = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true));
        for (int i=0; i<Math.Min(descendants.Count,32); i++) candidates.Add(descendants[i]);
        LastStatus = "no-text-pattern";
        foreach (var element in candidates)
        {
            var text = ReadElementSelection(element);
            if (text != null) return text;
        }
        return null;
    }
    private static string? ReadElementSelection(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var value))
            {
                var ranges = ((TextPattern)value).GetSelection();
                LastStatus = "selection-ranges-" + ranges.Length;
                if (ranges.Length > 0)
                {
                    // A caret-only range yields empty text; do not treat it as a selection.
                    string text = string.Join("\n", Array.ConvertAll(ranges, range => range.GetText(2001))).Trim();
                    if (text.Length > 2000) return text[..2001];
                    if (text.Length > 0) { LastStatus = "selected"; return text; }
                }
            }
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
        return null;
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
