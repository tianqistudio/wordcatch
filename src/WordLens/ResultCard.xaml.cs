using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WordLens;
public partial class ResultCard : UserControl
{
    private readonly WorkerClient worker;
    private readonly TranslationService translation;
    private CancellationTokenSource requestLifetime = new();
    private int generation;
    private bool assigning;
    private string origin = "输入文字";
    private bool IsSingleWord => Regex.IsMatch(Word.Text.Trim(), @"^[A-Za-z]+(?:[’'-][A-Za-z]+)*[.!?,;:]?$");
    private void SetBusy(bool busy) => Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    private void UpdateKind() { Kind.Text = IsSingleWord ? "单词释义" : "短语 / 句子"; Word.FontSize = IsSingleWord ? 23 : 16; }
    private Window? highlight;
    private readonly DispatcherTimer highlightTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    public ResultCard(WorkerClient worker, TranslationService? translation = null)
    {
        this.worker = worker; this.translation = translation ?? new TranslationService(); InitializeComponent(); Localization.Attach(this); Loaded += (_, _) => Localization.Attach(this);
        highlightTimer.Tick += (_, _) => ClearHighlight();
    }
    private void SetWord(string text) { assigning = true; Word.Text = text; assigning = false; UpdateKind(); }
    private void WordChanged(object sender, TextChangedEventArgs e)
    {
        if (!assigning && State != null) { generation++; CancelRequest(); State.Text = "已修改，按 Enter 重新查询。"; Phonetic.Text = ""; Definition.Text = ""; Source.Text = "待查询"; origin = "编辑原文"; Origin.Text = origin; UpdateKind(); }
    }
    private int Begin(string text, Native.Point point, string status, string origin)
    {
        int current = ++generation;
        CancelRequest();
        this.origin = origin; Origin.Text = origin;
        ClearHighlight(); SetWord(text); Phonetic.Text = ""; Definition.Text = ""; State.Text = status; Source.Text = "本地词典 · ECDICT"; SetBusy(true);
        return current;
    }
    public void ShowError(string message, string source)
    {
        Begin("", default, message, source);
        Kind.Text = "未捕获文字";
        Source.Text = source;
        SetBusy(false);
    }
    public async Task CaptureRegionAsync(Capture capture)
    {
        int current = Begin("", capture.Cursor, "正在识别框选区域…", "框选翻译");
        try
        {
            var reply = await worker.RequestAsync(new { action = "ocr_region", image = capture.Image });
            if (current != generation) return;
            string raw = reply.GetProperty("text").GetString() ?? "";
            string text = EnglishText.Extract(raw);
            SetWord(text);
            if (text.Length == 0) { State.Text = "框选区域未识别到英文，已忽略其他语言文字。"; return; }
            if (text != raw) reply = await worker.RequestAsync(new { action = "lookup", text });
            if (current != generation) return;
            if (text.Length > 2000) { State.Text = "识别文字超过 2,000 字符，请缩小框选区域。"; return; }
            await DisplayAsync(reply, current);
        }
        catch (Exception ex) { if (current == generation) State.Text = "框选识别失败：" + ex.Message; }
        finally { if (current == generation) SetBusy(false); }
    }

    public async Task CaptureAsync(Capture capture)
    {
        int current = Begin("", capture.Cursor, "正在识别鼠标附近的文字…", "屏幕识别");
        try
        {
            var reply = await worker.RequestAsync(new { action = "ocr", image = capture.Image, x = capture.X, y = capture.Y });
            if (current != generation) return;
            string text = reply.GetProperty("text").GetString() ?? "";
            SetWord(text);
            if (text.Length == 0) { Kind.Text = "未捕获文字"; Source.Text = "屏幕识别"; State.Text = "将鼠标移到文字上再试，也可以直接输入查询。"; return; }
            await DisplayAsync(reply, current);
            if (current != generation) return;
            if (reply.TryGetProperty("selected", out var selected) && selected.ValueKind == JsonValueKind.Object)
                Highlight(selected.GetProperty("box"), capture);
        }
        catch (Exception ex) { if (current == generation) State.Text = "识别失败，可修改原文重查：" + ex.Message; }
        finally { if (current == generation) SetBusy(false); }
    }
    public async Task LookupAsync(string text, Native.Point point, string origin = "输入文字")
    {
        int current = Begin(text, point, "正在查询本地词典…", origin);
        if (text.Length > 2000) { State.Text = "文字超过 2,000 字符，请缩短后重试。"; SetBusy(false); return; }
        try
        {
            var reply = await worker.RequestAsync(new { action = "lookup", text });
            if (current == generation) await DisplayAsync(reply, current);
        }
        catch (Exception ex) { if (current == generation) State.Text = "查词失败，可重试：" + ex.Message; }
        finally { if (current == generation) SetBusy(false); }
    }
    private async Task DisplayAsync(JsonElement reply, int current)
    {
        if (await TryLinesAsync(current)) return;
        var entry = reply.GetProperty("entry");
        if (entry.ValueKind == JsonValueKind.Null)
        {
            if (await TrySplitAsync(current)) return;
            if (current != generation) return;
            Source.Text = "本地词典 · 未收录";
            State.Text = IsSingleWord ? "本地未收录，可修改原文重查，或在设置中启用云端翻译。" : "句子翻译需设置云端服务，请在主窗口打开「设置与关于」。";
            if (translation.Enabled) await TranslateCloudAsync(current);
            return;
        }
        bool anki = entry.TryGetProperty("source", out var entrySource);
        if (anki) Source.Text = entrySource.GetString() ?? "Anki 本地短语库";
        string phonetic = entry.GetProperty("phonetic").GetString() ?? "";
        Phonetic.Text = phonetic.Length > 0 ? $"/{phonetic}/" : "";
        string matched = entry.GetProperty("matched").GetString() ?? "";
        State.Text = string.Equals(matched, Word.Text.Trim(), StringComparison.OrdinalIgnoreCase) ? "本地词典释义" : $"匹配词条：{matched}";
        Kind.Text = IsSingleWord ? "单词释义" : "短语释义";
        string chinese = entry.GetProperty("translation").GetString() ?? "";
        string english = entry.GetProperty("definition").GetString() ?? "";
        Definition.Text = chinese + (chinese.Length > 0 && english.Length > 0 ? "\n\n" : "") + english;
        if (chinese.Length == 0 && translation.Enabled && !anki) await TranslateCloudAsync(current);
    }
    private async Task<bool> TryLinesAsync(int current)
    {
        var lines = Regex.Split(Word.Text.Trim(), @"[\r\n]+|(?<=[.!?。！？])\s+")
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        if (lines.Length < 2) return false;
        var output = new StringBuilder();
        var cancellation = requestLifetime.Token;
        foreach (var line in lines)
        {
            if (current != generation) return true;
            if (output.Length > 0) output.AppendLine().AppendLine("────────────").AppendLine();
            output.AppendLine(line);
            var reply = await worker.RequestAsync(new { action = "lookup", text = line });
            if (current != generation) return true;
            var entry = reply.GetProperty("entry");
            if (entry.ValueKind != JsonValueKind.Null) AppendEntry(output, entry);
            else
            {
                var parts = CompoundWords.Split(line);
                if (parts.Length == 0 && translation.Enabled)
                {
                    try { output.AppendLine(await translation.TranslateAsync(line, cancellation)); }
                    catch (OperationCanceledException) { return true; }
                    catch (Exception ex) { output.AppendLine(Localization.Translate("翻译失败：" + ex.Message)); }
                }
                else
                {
                    if (parts.Length == 0) parts = Regex.Matches(line, @"[A-Za-z]+(?:[’'-][A-Za-z]+)*")
                        .Select(m => m.Value).Take(100).ToArray();
                    if (parts.Length == 0) output.AppendLine(Localization.Translate("本地未收录；整句翻译需启用云端服务。"));
                    foreach (var part in parts)
                    {
                        var item = await worker.RequestAsync(new { action = "lookup", text = part });
                        if (current != generation) return true;
                        output.Append(part).Append("：");
                        AppendEntry(output, item.GetProperty("entry"));
                    }
                }
            }
            if (current != generation) return true;
            Definition.Text = output.ToString();
        }
        Phonetic.Text = "";
        Kind.Text = "分行释义";
        Source.Text = translation.Enabled ? "逐行查询 · 本地词典 / 云端翻译" : "逐行查询 · 本地词典";
        State.Text = $"已分别处理 {lines.Length} 项；未收录的组合词拆分解释。";
        return true;
    }
    private static void AppendEntry(StringBuilder output, JsonElement entry)
    {
        if (entry.ValueKind == JsonValueKind.Null) { output.AppendLine(Localization.Translate("本地未收录")); return; }
        string text = entry.GetProperty("translation").GetString() ?? "";
        if (text.Length == 0) text = entry.GetProperty("definition").GetString() ?? "";
        output.AppendLine(text.Length > 0 ? text : Localization.Translate("暂无释义"));
    }
    private async Task<bool> TrySplitAsync(int current)
    {
        var parts = CompoundWords.Split(Word.Text);
        if (parts.Length == 0) return false;
        Kind.Text = "拆分释义";
        State.Text = $"完整词未收录，正在分别查询 {parts.Length} 个单词…";
        var definitions = new StringBuilder();
        int found = 0;
        foreach (string part in parts)
        {
            var reply = await worker.RequestAsync(new { action = "lookup", text = part });
            if (current != generation) return true;
            var entry = reply.GetProperty("entry");
            if (definitions.Length > 0) definitions.AppendLine().AppendLine("────────────").AppendLine();
            definitions.Append(part);
            if (entry.ValueKind == JsonValueKind.Null)
            {
                definitions.AppendLine().Append(Localization.Translate("本地词典未收录"));
                continue;
            }
            found++;
            string phonetic = entry.GetProperty("phonetic").GetString() ?? "";
            if (phonetic.Length > 0) definitions.Append("  /").Append(phonetic).Append('/');
            string matched = entry.GetProperty("matched").GetString() ?? "";
            if (!string.Equals(matched, part, StringComparison.OrdinalIgnoreCase))
                definitions.AppendLine().Append(Localization.Translate("匹配词条：")).Append(matched);
            string chinese = entry.GetProperty("translation").GetString() ?? "";
            string english = entry.GetProperty("definition").GetString() ?? "";
            if (chinese.Length > 0) definitions.AppendLine().Append(chinese);
            if (chinese.Length == 0 && english.Length > 0) definitions.AppendLine().Append(english);
            if (chinese.Length == 0 && english.Length == 0) definitions.AppendLine().Append(Localization.Translate("暂无释义"));
        }
        Definition.Text = definitions.ToString();
        Phonetic.Text = "";
        Source.Text = "本地词典 · ECDICT · 拆分查询";
        State.Text = found == parts.Length ? $"完整词未收录，已拆成 {parts.Length} 个单词分别解释。"
            : $"完整词未收录，拆分后查到 {found}/{parts.Length} 个单词；其余未收录。";
        return true;
    }
    private async Task TranslateCloudAsync(int current)
    {
        string text = Word.Text.Trim();
        var cancellation = requestLifetime.Token;
        if (!translation.Enabled) { State.Text = "请在主窗口的「设置与关于」启用 Azure 翻译。"; return; }
        SetBusy(true);
        State.Text = "正在翻译，可继续查询其他文字…";
        try
        {
            string translated = await translation.TranslateAsync(text, cancellation);
            if (current != generation || cancellation.IsCancellationRequested) return;
            Definition.Text = translated; Phonetic.Text = ""; Source.Text = "云端翻译 · Microsoft Azure"; Kind.Text = IsSingleWord ? "单词译文" : "句子译文"; State.Text = "已译为简体中文";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (current == generation) State.Text = ex.Message; }
        finally { if (current == generation) SetBusy(false); }
    }
    private async void CloudLookup(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Word.Text)) return;
        CancelRequest(); int current = ++generation;
        await TranslateCloudAsync(current);
    }
    private void CancelRequest() { if (Busy != null) SetBusy(false); requestLifetime.Cancel(); requestLifetime.Dispose(); requestLifetime = new CancellationTokenSource(); }
    private void Highlight(JsonElement box, Capture capture)
    {
        var points = box.EnumerateArray().Select(p => (X: p[0].GetDouble(), Y: p[1].GetDouble())).ToArray();
        int x = capture.Left+(int)Math.Floor(points.Min(p=>p.X))-2, y = capture.Top+(int)Math.Floor(points.Min(p=>p.Y))-2;
        int w = (int)Math.Ceiling(points.Max(p=>p.X)-points.Min(p=>p.X))+4, h = (int)Math.Ceiling(points.Max(p=>p.Y)-points.Min(p=>p.Y))+4;
        highlight = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowActivated = false, ShowInTaskbar = false, Topmost = true, IsHitTestVisible = false, Width=w, Height=h,
            Content = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(233,185,73)), BorderThickness = new Thickness(2), CornerRadius=new CornerRadius(2) } };
        highlight.Show();
        var handle = new System.Windows.Interop.WindowInteropHelper(highlight).Handle;
        // WS_EX_TRANSPARENT | WS_EX_NOACTIVATE: marker must never intercept the target application's mouse input.
        SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x20 | 0x08000000);
        Native.SetWindowPos(handle, new IntPtr(-1), x,y,w,h,0x0010);
        highlightTimer.Start();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr h,int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="SetWindowLongW")] private static extern int SetWindowLong(IntPtr h,int index,int value);
    private void ClearHighlight() { highlightTimer.Stop(); highlight?.Close(); highlight = null; }
    public void Dismiss() { generation++; CancelRequest(); ClearHighlight(); }
    private async void QueryAgain(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Word.Text)) return;
        Native.GetCursorPos(out var point); await LookupAsync(Word.Text.Trim(), point, "编辑原文");
    }
    private void WordKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled=true; QueryAgain(sender,e); } }
    private void CopyWord(object sender, RoutedEventArgs e)
    {
        try { if (Word.Text.Length > 0) { Clipboard.SetText(Word.Text); State.Text = "已复制原文。"; } }
        catch (System.Runtime.InteropServices.COMException) { State.Text = "剪贴板正忙，请重试。"; }
    }
}
