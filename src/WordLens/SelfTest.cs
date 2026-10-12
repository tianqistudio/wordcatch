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
        string output = Environment.GetEnvironmentVariable("WORDLENS_QA_OUTPUT") ?? Path.Combine(App.Root,"artifacts","qa","v0.4.0");
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
            await card.LookupAsync("Hello",capture.Cursor,"选中文字");
            await Task.Delay(150);
            checks["popup_does_not_activate"] = foreground == Native.GetForegroundWindow();
            checks["selection_origin_retained"] = card.Origin.Text == "选中文字";
            checks["word_layout"] = card.Kind.Text == "单词释义" && card.Definition.Text.Length > 0;
            Render(card,Path.Combine(output,"result-card.png"));
            card.Width = card.MinWidth; card.Height = card.MinHeight;
            await card.LookupAsync("This is a sentence about a curious little mouse.", capture.Cursor, "选中文字");
            checks["sentence_needs_cloud"] = card.State.Text.Contains("句子翻译需设置") && card.Busy.Visibility == Visibility.Collapsed;
            Render(card,Path.Combine(output,"sentence-disabled-minimum.png"));
            card.Word.Text = "Edited text";
            checks["edit_clears_old_result"] = card.Definition.Text == "" && card.Source.Text == "待查询";
            await card.LookupAsync(new string('x',2001),capture.Cursor,"选中文字");
            checks["long_selection_rejected"] = card.State.Text.Contains("2,000") && card.Busy.Visibility == Visibility.Collapsed;
            card.Width=460; card.Height=460;
            card.Dismiss();
            await card.CaptureAsync(capture);
            checks["highlight_does_not_activate"] = foreground == Native.GetForegroundWindow();
            checks["ocr_origin_retained"] = card.Origin.Text == "屏幕识别";
            var blankPoint = ((TextBlock)sample.Content).PointToScreen(new Point(500,140));
            var blank = ScreenCapture.AroundPoint(new Native.Point((int)blankPoint.X,(int)blankPoint.Y));
            card.Dismiss();
            await card.CaptureAsync(blank);
            checks["empty_ocr_actionable"] = card.Word.Text == "" && card.State.Text.Contains("移到文字上");
            Render(card,Path.Combine(output,"empty-ocr.png"));
            card.Dismiss();
            var fakeSettings = new CloudSettings(Path.Combine(output,"synthetic-cloud.json"));
            fakeSettings.Save(true,"eastasia","synthetic-not-a-real-key");
            var fakeHttp = new FixtureTranslator();
            var cloudCard = new ResultWindow(worker,new TranslationService(fakeSettings,new AzureTranslator(fakeHttp)));
            var pending = cloudCard.LookupAsync("A curious mouse reads a long sentence.",capture.Cursor,"选中文字");
            await fakeHttp.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            checks["cloud_loading_visible"] = cloudCard.Busy.Visibility == Visibility.Visible;
            Render(cloudCard,Path.Combine(output,"cloud-loading.png"));
            await cloudCard.LookupAsync("Hello",capture.Cursor,"选中文字");
            fakeHttp.Release.TrySetResult(true); await pending;
            checks["stale_cloud_cannot_overwrite"] = cloudCard.Word.Text == "Hello" && cloudCard.Source.Text.Contains("ECDICT") && cloudCard.Definition.Text != "这是一句模拟译文。";
            await cloudCard.LookupAsync("A curious mouse reads a long sentence.",capture.Cursor,"选中文字");
            checks["sentence_cloud_layout"] = cloudCard.Kind.Text == "句子译文" && cloudCard.Definition.Text == "这是一句模拟译文。";
            cloudCard.Width=cloudCard.MinWidth; cloudCard.Height=cloudCard.MinHeight;
            Render(cloudCard,Path.Combine(output,"sentence-translated-minimum.png"));
            checks["minimum_translation_visible"] = cloudCard.Definition.ActualHeight >= 28 && ((FrameworkElement)cloudCard.Definition.Parent).ActualHeight >= 56;
            fakeHttp.Fail = true;
            await cloudCard.LookupAsync("This simulated service request will fail.",capture.Cursor);
            checks["cloud_error_visible"] = cloudCard.State.Text.Contains("暂时不可用") && cloudCard.Busy.Visibility == Visibility.Collapsed;
            Render(cloudCard,Path.Combine(output,"cloud-error.png"));
            cloudCard.Dismiss();
            card.Dismiss();
            sample.Close(); sample=null;
            var main = new MainWindow(false); main.Show();
            for (int i=0; i<900 && !main.LookupButton.IsEnabled; i++) await Task.Delay(100);
            checks["main_ready"] = main.Status.Text.StartsWith("已就绪");
            Render(main,Path.Combine(output,"main-window.png"));
            main.Width=main.MinWidth; main.Height=main.MinHeight; main.UpdateLayout();
            Render(main,Path.Combine(output,"main-minimum.png"));
            checks["main_brand"] = main.Title == "单词捕手 · WordCatch";
            checks["minimum_query_visible"] = main.Query.ActualWidth > 150 && main.LookupButton.ActualWidth > 30;
            var settings = new CloudSettingsWindow(disabled) { Owner=main };
            settings.Show(); await Task.Delay(100); Render(settings,Path.Combine(output,"cloud-settings.png"));
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
