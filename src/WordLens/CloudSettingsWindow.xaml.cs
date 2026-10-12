using System;
using System.Threading;
using System.Windows;

namespace WordLens;
public partial class CloudSettingsWindow : Window
{
    private readonly TranslationService service;
    private readonly UpdateService updates;
    private readonly CancellationTokenSource lifetime = new();
    public CloudSettingsWindow(TranslationService service, UpdateService? updates=null)
    {
        this.service = service; this.updates=updates??new(); InitializeComponent();
        InstalledVersion.Text="当前版本 "+UpdateService.CurrentVersionText;
        AutoUpdate.IsChecked=this.updates.Settings.Options.AutoCheck;
        UpdateSourceChoice.SelectedIndex=(int)this.updates.Settings.Options.Source;
        RefreshUpdates(); this.updates.Changed+=RefreshUpdates;
        Enabled.IsChecked = service.Enabled; Region.Text = service.Settings.Options.Region;
        try { ApiKey.Password = service.Settings.GetKey(); } catch { }
        Message.Text = service.Settings.LoadError ?? "首次使用请从 Azure 资源的「密钥和终结点」复制密钥及区域。";
        Closed += (_,_) => { lifetime.Cancel(); this.updates.Changed-=RefreshUpdates; };
    }
    private void RefreshUpdates()
    {
        UpdateStatus.Text=updates.LastResult?.Message ?? updates.Settings.LoadError ?? "可手动检查；自动检查结果会在发现新版时提示。";
        DownloadUpdate.Visibility=updates.HasUpdate?Visibility.Visible:Visibility.Collapsed;
    }
    private async void CheckUpdateNow(object sender,RoutedEventArgs e)
    {
        CheckUpdate.IsEnabled=false; UpdateStatus.Text="正在检查更新…";
        try { await updates.CheckAsync((UpdateSource)UpdateSourceChoice.SelectedIndex); RefreshUpdates(); }
        catch(Exception) { UpdateStatus.Text="检查失败，请稍后重试。"; }
        finally { CheckUpdate.IsEnabled=true; }
    }
    private void OpenUpdate(object sender,RoutedEventArgs e)
    { try { updates.OpenRelease(); } catch(Exception) { UpdateStatus.Text="无法打开浏览器，请稍后重试。"; } }
    private void Save(object sender, RoutedEventArgs e)
    {
        try { service.Settings.Save(Enabled.IsChecked == true, Region.Text, ApiKey.Password); updates.Settings.Save(updates.Settings.Options with { AutoCheck=AutoUpdate.IsChecked==true, Source=(UpdateSource)UpdateSourceChoice.SelectedIndex }); DialogResult = true; }
        catch (Exception ex) { Message.Text = "保存失败：" + ex.Message; }
    }
    private void Cancel(object sender, RoutedEventArgs e) => Close();
    private async void TestConnection(object sender, RoutedEventArgs e)
    {
        Test.IsEnabled = false; Message.Text = "正在向 Azure 发送固定测试词 Hello…";
        try { var translated = await service.Client.TranslateAsync("Hello", ApiKey.Password.Trim(), Region.Text.Trim(), lifetime.Token); Message.Text = "连接成功：" + translated + "。点击保存后生效。"; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Message.Text = ex.Message; }
        finally { Test.IsEnabled = true; }
    }
}
