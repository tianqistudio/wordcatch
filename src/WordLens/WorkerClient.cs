using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WordLens;
public sealed class WorkerClient : IDisposable
{
    private Process? process;
    private readonly SemaphoreSlim gate = new(1, 1);
    private int sequence;
    private bool disposed;
    public bool Ready => process != null && !process.HasExited;
    public async Task StartAsync()
    {
        await gate.WaitAsync();
        try { await EnsureStarted(); } finally { gate.Release(); }
    }
    private async Task EnsureStarted()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Ready) return;
        Stop();
        string frozen = Path.Combine(App.Root, "worker", "WordLens.Worker.exe");
        bool packaged = File.Exists(frozen);
        var start = new ProcessStartInfo(packaged ? frozen : Path.Combine(App.Root, ".venv", "Scripts", "python.exe"))
        {
            WorkingDirectory = App.Root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (!packaged) { start.ArgumentList.Add("-u"); start.ArgumentList.Add(Path.Combine(App.Root, "worker", "server.py")); }
        start.Environment["PYTHONUTF8"] = "1";
        process = Process.Start(start) ?? throw new IOException("无法启动识别进程。");
        // Drain library diagnostics without storing screenshots, words or query history.
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        try
        {
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(90));
            using var doc = JsonDocument.Parse(line ?? throw new IOException("识别进程已退出。"));
            if (!doc.RootElement.TryGetProperty("ready", out var ready) || !ready.GetBoolean())
                throw new IOException(doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() : "初始化失败。");
        }
        catch { Stop(); throw; }
    }
    public async Task<JsonElement> RequestAsync(object payload)
    {
        await gate.WaitAsync();
        try
        {
            await EnsureStarted();
            var node = System.Text.Json.JsonSerializer.SerializeToNode(payload)!;
            int id = ++sequence;
            node["id"] = id;
            try
            {
                await process!.StandardInput.WriteLineAsync(node.ToJsonString());
                await process.StandardInput.FlushAsync();
                var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
                using var doc = JsonDocument.Parse(line ?? throw new IOException("识别进程意外退出。"));
                var result = doc.RootElement;
                if (result.GetProperty("id").GetInt32() != id) throw new IOException("识别响应顺序异常。");
                if (result.TryGetProperty("error", out var error)) throw new IOException(error.GetString());
                return result.Clone();
            }
            catch { Stop(); throw; }
        }
        finally { gate.Release(); }
    }
    private void Stop()
    {
        if (process == null) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        process.Dispose(); process = null;
    }
    public void Dispose() { disposed = true; Stop(); }
}
