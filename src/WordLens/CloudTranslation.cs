using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WordLens;
public sealed record CloudOptions(bool Enabled = false, string Region = "", string ProtectedKey = "");

public sealed class CloudSettings
{
    private readonly string path;
    public CloudOptions Options { get; private set; } = new();
    public string? LoadError { get; private set; }
    public CloudSettings(string? path = null)
    {
        this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TianqiStudio", "WordLens", "cloud.json");
        try
        {
            if (File.Exists(this.path)) Options = JsonSerializer.Deserialize<CloudOptions>(File.ReadAllText(this.path)) ?? throw new InvalidDataException();
            if (Options.Enabled) _ = GetKey();
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or FormatException or UnauthorizedAccessException)
        { Options = new(); LoadError = "云端配置无法读取，原文件已保留；请重新填写并保存。"; }
    }
    public string GetKey() => string.IsNullOrEmpty(Options.ProtectedKey) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(Options.ProtectedKey), null, DataProtectionScope.CurrentUser));
    public void Save(bool enabled, string region, string key)
    {
        region = region.Trim(); key = key.Trim();
        if (enabled && string.IsNullOrEmpty(key)) throw new ArgumentException("请填写 Azure Translator 密钥。");
        if (!Regex.IsMatch(region, "^[a-zA-Z0-9-]{0,64}$")) throw new ArgumentException("区域名称只能包含字母、数字和连字符。");
        if (key.Length > 512 || key.Contains('\r') || key.Contains('\n')) throw new ArgumentException("密钥格式不正确。");
        var next = new CloudOptions(enabled, region, key.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser)));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Persist a single encrypted document atomically. Never write plaintext credentials.
        string temporary = path + ".new";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(file, next); file.Flush(true); }
        File.Move(temporary, path, true);
        Options = next; LoadError = null;
    }
}

public sealed class AzureTranslator
{
    private readonly HttpClient client;
    public AzureTranslator(HttpMessageHandler? handler = null)
    {
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 1024*1024 };
    }
    public async Task<string> TranslateAsync(string text, string key, string region, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("请先在「云端翻译设置」填写 Azure 密钥。");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new ArgumentException("每次翻译需要 1～2000 个字符。");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to=zh-Hans");
        request.Headers.Add("Ocp-Apim-Subscription-Key", key);
        if (region.Length > 0) request.Headers.Add("Ocp-Apim-Subscription-Region", region);
        request.Content = new StringContent(JsonSerializer.Serialize(new[] { new { Text = text } }), Encoding.UTF8, "application/json");
        try
        {
            using var response = await client.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Azure 密钥无效，请检查设置。",
                HttpStatusCode.Forbidden => "Azure 拒绝访问，请检查订阅权限、区域或额度。",
                HttpStatusCode.TooManyRequests => "Azure 请求过多或额度不足，请稍后重试。",
                _ => $"Azure 暂时不可用（HTTP {(int)response.StatusCode}）。"
            });
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var result = document.RootElement[0].GetProperty("translations")[0].GetProperty("text").GetString();
            return string.IsNullOrWhiteSpace(result) ? throw new InvalidOperationException("Azure 返回了空译文。") : result;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new InvalidOperationException("Azure 请求超时，本地查词仍可使用。"); }
        catch (HttpRequestException) { throw new InvalidOperationException("无法连接 Azure，请检查网络；本地查词仍可使用。"); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException) { throw new InvalidOperationException("Azure 响应格式不正确，请稍后重试。"); }
    }
}

public sealed class TranslationService
{
    public CloudSettings Settings { get; }
    public AzureTranslator Client { get; }
    public TranslationService(CloudSettings? settings = null, AzureTranslator? client = null)
    { Settings = settings ?? new CloudSettings(); Client = client ?? new AzureTranslator(); }
    public bool Enabled => Settings.Options.Enabled;
    public Task<string> TranslateAsync(string text, CancellationToken cancellation = default)
    {
        if (!Enabled) throw new InvalidOperationException("云端翻译未启用，请在主窗口中设置 Azure Translator。");
        return Client.TranslateAsync(text, Settings.GetKey(), Settings.Options.Region, cancellation);
    }
}
