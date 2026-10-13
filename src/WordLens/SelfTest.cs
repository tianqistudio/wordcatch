using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TianqiStudio.DesktopShortcuts;
using TianqiStudio.DesktopShortcuts.Wpf;

namespace WordLens;
public static class SelfTest
{
    public static async Task RunAsync()
    {
        var checks = new Dictionary<string, object>();
        string output = Environment.GetEnvironmentVariable("WORDLENS_QA_OUTPUT") ?? Path.Combine(App.Root,"artifacts","qa","v0.5.0");
        Directory.CreateDirectory(output);
        var monitors = new List<object>();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var monitor = Native.MonitorFromPoint(new Native.Point(screen.Bounds.Left+10,screen.Bounds.Top+10),2);
            Native.GetDpiForMonitor(monitor,0,out uint dpi,out _);
            monitors.Add(new { width=screen.Bounds.Width,height=screen.Bounds.Height,dpi });
        }
        checks["display_environment"] = monitors;
        using var worker = new WorkerClient();
        Window? sample = null;
        ResultWindow? card = null;
        try
        {
            foreach (bool empty in new[] { false, true })
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false, CreateNoWindow=true };
                start.ArgumentList.Add("--selection-fixture");
                if (empty) start.ArgumentList.Add("--empty");
                using var fixture = Process.Start(start)!;
                try
                {
                    for (int ready=0; ready<60; ready++)
                    {
                        fixture.Refresh();
                        if (fixture.HasExited) throw new Exception("Selection fixture exited before its window was ready");
                        if (fixture.MainWindowHandle != IntPtr.Zero) break;
                        await Task.Delay(100);
                    }
                    if (fixture.MainWindowHandle == IntPtr.Zero) throw new Exception("Selection fixture window did not become ready");
                    await Task.Delay(250);
                    string? selection = await SelectedText.ReadFromWindowAsync(fixture.MainWindowHandle, false);
                    checks[empty?"caret_status":"selection_status"] = SelectedText.LastStatus;
                    if(!empty) checks["selection_value"] = selection ?? "";
                    checks[empty?"caret_not_selection":"external_selected_text"] = empty ? selection == null : selection == "Hello world";
                }
                finally { if(!fixture.HasExited) fixture.Kill(); await fixture.WaitForExitAsync(); }
            }
            // Synthetic test content only: capture the application's own test surface.
            sample = new Window { Title="WordLens synthetic test",Width=820,Height=300,Left=100,Top=100,Background=Brushes.White,Topmost=true,
                Content=new TextBlock { Text="Hello world",FontSize=36,Foreground=Brushes.Black,Margin=new Thickness(40,60,0,0),FontFamily=new FontFamily("Segoe UI") } };
            sample.Show(); sample.Activate();
            await Task.Delay(400);
            var point = ((TextBlock)sample.Content).PointToScreen(new Point(36,24));
            var capture = ScreenCapture.AroundPoint(new Native.Point((int)point.X,(int)point.Y));
            await worker.StartAsync();
            var reply = await worker.RequestAsync(new {action="ocr",image=capture.Image,x=capture.X,y=capture.Y});
            checks["capture_text"] = reply.GetProperty("text").GetString() ?? "";
            if (!string.Equals(reply.GetProperty("text").GetString(),"Hello",StringComparison.OrdinalIgnoreCase)) throw new Exception("Real screen OCR did not hit Hello: " + reply);
            if (reply.GetProperty("entry").ValueKind!=JsonValueKind.Object) throw new Exception("Dictionary lookup failed");
            checks["capture_ocr_dictionary"] = true;
            checks["ocr_ms"] = reply.GetProperty("elapsed_ms").GetInt32();
            using (var registrar = new WindowsHotkeyRegistrar())
            {
                var chord = new ShortcutChord(0x79,ShortcutModifiers.Control|ShortcutModifiers.Alt|ShortcutModifiers.Shift);
                if (!registrar.TryRegister(91,chord,out var error)) throw new Exception(error);
                using var conflict = new WindowsHotkeyRegistrar();
                checks["native_conflict_rejected"] = !conflict.TryRegister(92,chord,out _);
                registrar.Unregister(91);
                checks["native_release"] = conflict.TryRegister(92,chord,out _);
            }
            var foreground = Native.GetForegroundWindow();
            var disabled = new TranslationService(new CloudSettings(Path.Combine(output,"absent-settings.json")));
            card = new ResultWindow(worker, disabled);
            await card.CaptureRegionAsync(capture);
            checks["region_text_recognized"] = card.Primary.Word.Text.Contains("Hello");
            checks["region_origin"] = card.Primary.Origin.Text == "框选翻译";
            checks["region_cloud_disabled_hint"] = card.Primary.State.Text.Contains("云端");
            Render(card,Path.Combine(output,"region-result.png"));
            await card.LookupAsync("Hello",capture.Cursor,"选中文字");
            await Task.Delay(150);
            checks["popup_does_not_activate"] = foreground == Native.GetForegroundWindow();
            checks["selection_origin_retained"] = card.Primary.Origin.Text == "选中文字";
            checks["word_layout"] = card.Primary.Kind.Text == "单词释义" && card.Primary.Definition.Text.Length > 0;
            Render(card,Path.Combine(output,"result-card.png"));
            checks["compound_boundaries"] = string.Join("|", CompoundWords.Split("response-conventions")) == "response|conventions"
                && string.Join("|", CompoundWords.Split("responseConventions")) == "response|Conventions"
                && string.Join("|", CompoundWords.Split("HTTPServer")) == "HTTP|Server"
                && string.Join("|", CompoundWords.Split("response_conventions")) == "response|conventions";
            checks["no_arbitrary_splitting"] = CompoundWords.Split("unknownword").Length == 0
                && CompoundWords.Split("don't").Length == 0 && CompoundWords.Split("This is a sentence.").Length == 0;
            await card.LookupAsync("response-conventions", capture.Cursor, "屏幕识别");
            checks["compound_explained_separately"] = card.Primary.Kind.Text == "拆分释义"
                && card.Primary.Definition.Text.Contains("response") && card.Primary.Definition.Text.Contains("conventions")
                && card.Primary.State.Text.Contains("2 个单词");
            Render(card, Path.Combine(output, "compound-split.png"));
            await card.LookupAsync("response-zzzznotaword", capture.Cursor);
            checks["partial_split_honest"] = card.Primary.State.Text.Contains("1/2") && card.Primary.Definition.Text.Contains("未收录");
            await card.LookupAsync("mother-in-law", capture.Cursor);
            checks["known_compound_not_split"] = card.Primary.Kind.Text != "拆分释义" && card.Primary.Definition.Text.Length > 0;
            checks["clipboard_trimmed"] = ClipboardText.Read(() => "  world\r\n").Text == "world";
            checks["clipboard_empty"] = ClipboardText.Read(() => " \r\n").Text == null;
            checks["clipboard_limit"] = ClipboardText.Read(() => new string('x', 2000)).Text?.Length == 2000
                && ClipboardText.Read(() => new string('x', 2001)).Error != null;
            checks["clipboard_busy"] = ClipboardText.Read(() => throw new System.Runtime.InteropServices.COMException()).Error != null;
            await card.LookupSourcesAsync("Hello", null, new ClipboardRead("world"), capture.Cursor);
            checks["both_sources_visible"] = card.Primary.Origin.Text == "选中文字" && card.ClipboardResult.Origin.Text == "剪贴板"
                && card.Primary.Definition.Text.Length > 0 && card.ClipboardResult.Definition.Text.Length > 0
                && card.ClipboardBorder.IsVisible;
            checks["combined_does_not_activate"] = foreground == Native.GetForegroundWindow();
            Render(card, Path.Combine(output, "clipboard-dual.png"));
            card.Width = card.MinWidth; card.Height = card.MinHeight;
            Render(card, Path.Combine(output, "clipboard-dual-minimum.png"));
            checks["narrow_cards_stacked"] = Grid.GetRow(card.ClipboardBorder) == 1 && card.ClipboardResult.Word.ActualWidth > 150;
            card.ClipboardResult.Word.Text = "mouse";
            checks["independent_edit"] = card.ClipboardResult.Definition.Text == "" && card.Primary.Definition.Text.Length > 0;
            await card.LookupSourcesAsync("Hello", null, new ClipboardRead("Hello"), capture.Cursor);
            checks["same_word_both_origins"] = card.Primary.Word.Text == "Hello" && card.ClipboardResult.Word.Text == "Hello";
            await card.LookupSourcesAsync(null, capture, new ClipboardRead("world"), capture.Cursor);
            checks["ocr_and_clipboard"] = card.Primary.Word.Text.Equals("Hello", StringComparison.OrdinalIgnoreCase)
                && card.ClipboardResult.Word.Text == "world" && card.ClipboardResult.Definition.Text.Length > 0;
            await card.LookupSourcesAsync(null, null, new ClipboardRead("world"), capture.Cursor, "屏幕取词失败");
            checks["clipboard_survives_capture_failure"] = card.Primary.State.Text == "屏幕取词失败" && card.ClipboardResult.Definition.Text.Length > 0;
            await card.LookupSourcesAsync("Hello", null, new ClipboardRead(Error: "剪贴板正忙"), capture.Cursor);
            checks["screen_survives_clipboard_failure"] = card.Primary.Definition.Text.Length > 0 && card.ClipboardResult.State.Text == "剪贴板正忙";
            Render(card, Path.Combine(output, "clipboard-busy.png"));
            await card.LookupSourcesAsync("Hello", null, new ClipboardRead(), capture.Cursor);
            checks["empty_clipboard_hidden"] = !card.ClipboardBorder.IsVisible;
            card.Width = card.MinWidth; card.Height = card.MinHeight;
            await card.LookupAsync("This is a sentence about a curious little mouse.", capture.Cursor, "选中文字");
            checks["sentence_needs_cloud"] = card.Primary.State.Text.Contains("句子翻译需设置") && card.Primary.Busy.Visibility == Visibility.Collapsed;
            Render(card,Path.Combine(output,"sentence-disabled-minimum.png"));
            card.Primary.Word.Text = "Edited text";
            checks["edit_clears_old_result"] = card.Primary.Definition.Text == "" && card.Primary.Source.Text == "待查询";
            await card.LookupAsync(new string('x',2001),capture.Cursor,"选中文字");
            checks["long_selection_rejected"] = card.Primary.State.Text.Contains("2,000") && card.Primary.Busy.Visibility == Visibility.Collapsed;
            card.Width=460; card.Height=460;
            card.Dismiss();
            await card.CaptureAsync(capture);
            checks["highlight_does_not_activate"] = foreground == Native.GetForegroundWindow();
            checks["ocr_origin_retained"] = card.Primary.Origin.Text == "屏幕识别";
            var blankPoint = ((TextBlock)sample.Content).PointToScreen(new Point(500,140));
            var blank = ScreenCapture.AroundPoint(new Native.Point((int)blankPoint.X,(int)blankPoint.Y));
            card.Dismiss();
            await card.CaptureAsync(blank);
            checks["empty_ocr_actionable"] = card.Primary.Word.Text == "" && card.Primary.State.Text.Contains("移到文字上");
            Render(card,Path.Combine(output,"empty-ocr.png"));
            card.Dismiss();
            var fakeSettings = new CloudSettings(Path.Combine(output,"synthetic-cloud.json"));
            fakeSettings.Save(true,"eastasia","synthetic-not-a-real-key");
            var fakeHttp = new FixtureTranslator();
            var cloudCard = new ResultWindow(worker,new TranslationService(fakeSettings,new AzureTranslator(fakeHttp)));
            var pending = cloudCard.LookupAsync("A curious mouse reads a long sentence.",capture.Cursor,"选中文字");
            await fakeHttp.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            checks["cloud_loading_visible"] = cloudCard.Primary.Busy.Visibility == Visibility.Visible;
            Render(cloudCard,Path.Combine(output,"cloud-loading.png"));
            await cloudCard.LookupAsync("Hello",capture.Cursor,"选中文字");
            fakeHttp.Release.TrySetResult(true); await pending;
            checks["stale_cloud_cannot_overwrite"] = cloudCard.Primary.Word.Text == "Hello" && cloudCard.Primary.Source.Text.Contains("ECDICT") && cloudCard.Primary.Definition.Text != "这是一句模拟译文。";
            var delayedHttp = new FixtureTranslator();
            var dualCloud = new ResultWindow(worker, new TranslationService(fakeSettings, new AzureTranslator(delayedHttp)));
            var delayed = dualCloud.LookupSourcesAsync("Hello", null, new ClipboardRead("A curious mouse reads a long sentence."), capture.Cursor);
            await delayedHttp.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            checks["independent_cloud_loading"] = dualCloud.Primary.Definition.Text.Length > 0 && dualCloud.ClipboardResult.Busy.Visibility == Visibility.Visible;
            await dualCloud.LookupSourcesAsync("world", null, new ClipboardRead("Hello"), capture.Cursor);
            delayedHttp.Release.TrySetResult(true); await delayed;
            checks["stale_clipboard_cloud_cannot_overwrite"] = dualCloud.ClipboardResult.Word.Text == "Hello"
                && dualCloud.ClipboardResult.Source.Text.Contains("ECDICT") && dualCloud.Primary.Word.Text == "world";
            dualCloud.Dismiss();
            await cloudCard.LookupAsync("A curious mouse reads a long sentence.",capture.Cursor,"选中文字");
            checks["sentence_cloud_layout"] = cloudCard.Primary.Kind.Text == "句子译文" && cloudCard.Primary.Definition.Text == "这是一句模拟译文。";
            cloudCard.Width=cloudCard.MinWidth; cloudCard.Height=cloudCard.MinHeight;
            Render(cloudCard,Path.Combine(output,"sentence-translated-minimum.png"));
            checks["minimum_translation_visible"] = cloudCard.Primary.Definition.ActualHeight >= 28 && ((FrameworkElement)cloudCard.Primary.Definition.Parent).ActualHeight >= 56;
            fakeHttp.Fail = true;
            await cloudCard.LookupAsync("This simulated service request will fail.",capture.Cursor);
            checks["cloud_error_visible"] = cloudCard.Primary.State.Text.Contains("暂时不可用") && cloudCard.Primary.Busy.Visibility == Visibility.Collapsed;
            Render(cloudCard,Path.Combine(output,"cloud-error.png"));
            cloudCard.Dismiss();
            card.Dismiss();
            sample.Close(); sample=null;
            var startupTestKey = @"Software\TianqiStudio\WordLens\Tests\" + Guid.NewGuid().ToString("N");
            try
            {
                var startup = new StartupSettings(startupTestKey);
                checks["startup_default_off"] = !startup.Enabled;
                startup.Save(true);
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(startupTestKey))
                    checks["startup_command_quoted"] = (string?)key?.GetValue(StartupSettings.ValueName) == StartupSettings.Command(Environment.ProcessPath!);
                checks["startup_enabled_saved"] = new StartupSettings(startupTestKey).Enabled;
                startup.Save(false);
                checks["startup_disabled_saved"] = !new StartupSettings(startupTestKey).Enabled;
            }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(startupTestKey, false); }
            var main = new MainWindow(false);
            await main.InitializeAsync(false);
            checks["startup_hidden_initialized"] = !main.IsVisible && main.LookupButton.IsEnabled;
            main.Show();
            for (int i=0; i<900 && !main.LookupButton.IsEnabled; i++) await Task.Delay(100);
            checks["main_ready"] = main.Status.Text.StartsWith("已就绪");
            Render(main,Path.Combine(output,"main-window.png"));
            main.Width=main.MinWidth; main.Height=main.MinHeight; main.UpdateLayout();
            Render(main,Path.Combine(output,"main-minimum.png"));
            checks["main_brand"] = main.Title == "单词捕手 · WordCatch";
            checks["minimum_query_visible"] = main.Query.ActualWidth > 150 && main.LookupButton.ActualWidth > 30;
            var settings = new CloudSettingsWindow(disabled) { Owner=main };
            settings.Show(); await Task.Delay(100); Render(settings,Path.Combine(output,"cloud-settings.png"));
            checks["startup_option_visible"] = settings.StartAtLogin.IsVisible;
            checks["update_settings_visible"] = settings.CheckUpdate.IsVisible && settings.UpdateSourceChoice.Items.Count==3;
            checks["update_auto_default"] = settings.AutoUpdate.IsChecked==true;
            settings.Close();
            checks["passed"] = !checks.ContainsValue(false);
            File.WriteAllText(Path.Combine(output,"desktop-self-test.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions {WriteIndented=true}));
            Application.Current.Shutdown(checks.ContainsValue(false)?1:0);
        }
        catch (Exception ex)
        {
            checks["passed"]=false; checks["error"]=ex.ToString();
            File.WriteAllText(Path.Combine(output,"desktop-self-test.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions {WriteIndented=true}));
            card?.Dismiss(); sample?.Close(); Application.Current.Shutdown(1);
        }
    }
    private sealed class FixtureTranslator : HttpMessageHandler
    {
        public readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Fail;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            // Deliberately finish late to exercise result generation guards.
            await Release.Task;
            return new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent("[{\"translations\":[{\"text\":\"这是一句模拟译文。\"}]}]") };
        }
    }
    private static void Render(Window window,string path)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
        image.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); png.Save(stream);
    }
}
