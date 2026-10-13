using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WordLens;
public enum UpdateSource { Auto, Website, GitHub }
public sealed record UpdateOptions(bool AutoCheck = true, UpdateSource Source = UpdateSource.Auto, DateTimeOffset? LastCheckUtc = null);
public sealed class UpdateSettings
{
    private readonly string path;
    public UpdateOptions Options { get; private set; } = new();
    public string? LoadError { get; private set; }
    public UpdateSettings(string? path = null)
    {
        this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TianqiStudio","WordLens","updates.json");
        try
        {
            if (File.Exists(this.path)) Options = JsonSerializer.Deserialize<UpdateOptions>(File.ReadAllText(this.path)) ?? throw new InvalidDataException();
            if (!Enum.IsDefined(Options.Source)) throw new InvalidDataException();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { Options = new(); LoadError = "更新设置无法读取，请重新保存。"; }
    }
    public void Save(UpdateOptions options)
    {
        if (!Enum.IsDefined(options.Source)) throw new ArgumentException("更新源无效。");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path+".new",JsonSerializer.Serialize(options));
        File.Move(path+".new",path,true); Options = options; LoadError = null;
    }
}
public sealed record ReleaseInfo(Version Version, string VersionText, Uri Page, string Source, string? Sha256);
public sealed record UpdateResult(ReleaseInfo? Release, string Message, bool AnySourceSucceeded);
public sealed class UpdateClient
{
    public const string WebsiteUrl = "https://tianqistudio.com/updates/wordcatch/latest.json";
    public const string GitHubUrl = "https://api.github.com/repos/tianqistudio/wordcatch/releases/latest";
    public const string RepositoryUrl = "https://github.com/tianqistudio/wordcatch";
    private readonly HttpClient http;
    public UpdateClient(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WordCatch/0.6.1");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }
    public static Version ParseVersion(string value)
    {
        if (!Regex.IsMatch(value,@"^\d{1,6}\.\d{1,6}\.\d{1,6}(\.\d{1,6})?$")) throw new FormatException("版本格式无效");
        var v=Version.Parse(value); return new Version(v.Major,v.Minor,v.Build,Math.Max(0,v.Revision));
    }
    public static ReleaseInfo Parse(string json, bool github)
    {
        using var doc=JsonDocument.Parse(json); var root=doc.RootElement;
        string version;
        if (github)
        {
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) throw new FormatException("不是稳定版本");
            var tag=root.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith("v",StringComparison.Ordinal)) throw new FormatException("标签格式无效");
            version=tag.Substring(1);
        }
        else
        {
            if (root.GetProperty("schema_version").GetInt32()!=1 || root.GetProperty("product").GetString()!="wordcatch") throw new FormatException("更新清单不匹配");
            version=root.GetProperty("version").GetString() ?? "";
        }
        var parsed=ParseVersion(version);
        string expectedPage=RepositoryUrl+"/releases/tag/v"+version;
        string page=root.GetProperty(github?"html_url":"release_url").GetString() ?? "";
        if (page!=expectedPage) throw new FormatException("发布地址不匹配");
        string expectedDownload=RepositoryUrl+"/releases/download/v"+version+"/WordCatch-"+version+"-Setup.exe";
        string? sha=null;
        if (github)
        {
            var asset=root.GetProperty("assets").EnumerateArray().FirstOrDefault(x => x.GetProperty("name").GetString()=="WordCatch-"+version+"-Setup.exe");
            if (asset.ValueKind==JsonValueKind.Undefined || asset.GetProperty("browser_download_url").GetString()!=expectedDownload || asset.GetProperty("size").GetInt64()<=0 || asset.GetProperty("state").GetString()!="uploaded") throw new FormatException("缺少安装包");
            if (asset.TryGetProperty("digest",out var digest) && digest.ValueKind==JsonValueKind.String)
            { var value=digest.GetString()!; if (value.StartsWith("sha256:",StringComparison.Ordinal)) sha=value.Substring(7); }
        }
        else
        {
            if (root.GetProperty("download_url").GetString()!=expectedDownload) throw new FormatException("下载地址不匹配");
            sha=root.GetProperty("sha256").GetString();
            if (sha==null) throw new FormatException("缺少校验值");
        }
        if (sha!=null && !Regex.IsMatch(sha,@"^[a-fA-F0-9]{64}$")) throw new FormatException("校验值无效");
        return new ReleaseInfo(parsed,version,new Uri(expectedPage),github?"GitHub":"tianqistudio.com",sha);
    }
    private async Task<ReleaseInfo?> ReadAsync(bool github,CancellationToken token)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            using var response=await http.GetAsync(github?GitHubUrl:WebsiteUrl,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength>262144) throw new FormatException("响应过大");
            using var stream=await response.Content.ReadAsStreamAsync(deadline.Token); using var buffer=new MemoryStream();
            var bytes=new byte[8192]; int n;
            while ((n=await stream.ReadAsync(bytes,deadline.Token))>0)
            { if(buffer.Length+n>262144) throw new FormatException("响应过大"); buffer.Write(bytes,0,n); }
            return Parse(System.Text.Encoding.UTF8.GetString(buffer.ToArray()),github);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException or OverflowException) { return null; }
    }
    public async Task<UpdateResult> CheckAsync(UpdateSource source, Version current, CancellationToken token=default)
    {
        if(!Enum.IsDefined(source)) throw new ArgumentException("更新源无效");
        ReleaseInfo?[] results;
        try
        {
            results=source switch {
                UpdateSource.Website => new[] { await ReadAsync(false,token) },
                UpdateSource.GitHub => new[] { await ReadAsync(true,token) },
                _ => await Task.WhenAll(ReadAsync(false,token),ReadAsync(true,token))
            };
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(null,"检查超时，稍后可重试；查词不受影响。",false); }
        var good=results.Where(x=>x!=null).Cast<ReleaseInfo>().OrderByDescending(x=>x.Version).ToArray();
        if(good.Length==0) return new(null,"暂时无法连接更新源，请稍后重试。",false);
        var newest=good[0];
        if(good.Length==2 && good[0].Version==good[1].Version && good[0].Sha256!=null && good[1].Sha256!=null && !string.Equals(good[0].Sha256,good[1].Sha256,StringComparison.OrdinalIgnoreCase))
            return new(null,"两个更新源的校验值不一致，请稍后重试。",false);
        string fallback=source==UpdateSource.Auto && good.Length==1 ? "（另一个来源暂不可用）" : "";
        return new(newest,newest.Version>current ? "发现新版 "+newest.VersionText+" · "+newest.Source+fallback : "未发现新版 · "+newest.Source+fallback,true);
    }
}
public sealed class UpdateService
{
    public static Version CurrentVersion => UpdateClient.ParseVersion(typeof(UpdateService).Assembly.GetName().Version!.ToString());
    public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";
    public UpdateSettings Settings { get; }
    private readonly UpdateClient client;
    private Task<UpdateResult>? pending;
    public UpdateResult? LastResult { get; private set; }
    public event Action? Changed;
    public bool Checking => pending is {IsCompleted:false};
    public bool HasUpdate => LastResult?.Release?.Version>CurrentVersion;
    public UpdateService(UpdateSettings? settings=null,UpdateClient? client=null) { Settings=settings??new();this.client=client??new(); }
    public Task<UpdateResult> CheckAsync(UpdateSource? source=null)
    { if(pending is {IsCompleted:false}) return pending; pending=RunAsync(source??Settings.Options.Source); return pending; }
    private async Task<UpdateResult> RunAsync(UpdateSource source)
    {
        var result=await client.CheckAsync(source,CurrentVersion);
        LastResult=result;
        try { Settings.Save(Settings.Options with {LastCheckUtc=DateTimeOffset.UtcNow}); }
        catch (Exception ex) when(ex is IOException or UnauthorizedAccessException) { LastResult=result with {Message=result.Message+"；检查时间未能保存。"}; }
        Changed?.Invoke(); return LastResult;
    }
    public Task CheckAutomaticAsync()
    {
        var o=Settings.Options; var now=DateTimeOffset.UtcNow;
        if(!o.AutoCheck || (o.LastCheckUtc is { } last && last<=now && now-last<TimeSpan.FromHours(6))) return Task.CompletedTask;
        return CheckAsync();
    }
    public void OpenRelease()
    {
        if(HasUpdate && LastResult?.Release is { } release)
            Process.Start(new ProcessStartInfo(release.Page.AbsoluteUri){UseShellExecute=true});
    }
}
