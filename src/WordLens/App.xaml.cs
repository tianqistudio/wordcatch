using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace WordLens;
public partial class App : Application
{
    private Mutex? instance;
    public static string Root { get; private set; } = "";
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool selfTest = Array.IndexOf(e.Args, "--self-test") >= 0;
        bool fixture = Array.IndexOf(e.Args, "--selection-fixture") >= 0;
        if (fixture) { SelectionFixture.Show(Array.IndexOf(e.Args,"--empty") >= 0); return; }
        instance = new Mutex(true, selfTest ? "Local\\TianqiStudio.WordLens.SelfTest" : "Local\\TianqiStudio.WordLens", out bool first);
        if (!first) { MessageBox.Show("单词捕手已在运行，请从通知区域打开。", "单词捕手 · WordCatch"); Shutdown(); return; }
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "worker", "server.py")) && !File.Exists(Path.Combine(dir.FullName, "worker", "WordLens.Worker.exe"))) dir = dir.Parent;
        if (dir == null) { MessageBox.Show("找不到项目运行文件，请使用项目启动脚本。", "单词捕手 · WordCatch"); Shutdown(1); return; }
        Root = dir.FullName;
        if (selfTest)
        {
            _ = SelfTest.RunAsync();
            return;
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
