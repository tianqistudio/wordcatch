using System.Net;
using System.Text.Json;
using WordLens;
var checks=new Dictionary<string,bool>();
var now=UpdateClient.ParseVersion("0.4.0");
string Site(string v,string hash="a") => JsonSerializer.Serialize(new {schema_version=1,product="wordcatch",version=v,release_url=UpdateClient.RepositoryUrl+"/releases/tag/v"+v,download_url=UpdateClient.RepositoryUrl+"/releases/download/v"+v+"/WordCatch-"+v+"-Setup.exe",sha256=new string(hash[0],64)});
string Git(string v) => JsonSerializer.Serialize(new {tag_name="v"+v,draft=false,prerelease=false,html_url=UpdateClient.RepositoryUrl+"/releases/tag/v"+v,assets=new[]{new {name="WordCatch-"+v+"-Setup.exe",state="uploaded",size=123,digest="sha256:"+new string('a',64),browser_download_url=UpdateClient.RepositoryUrl+"/releases/download/v"+v+"/WordCatch-"+v+"-Setup.exe"}}});
var handler=new Stub {Site=Site("0.4.0"),Git=Git("0.5.0")};var client=new UpdateClient(handler);
var result=await client.CheckAsync(UpdateSource.Auto,now);checks["higher_github_version_wins"]=result.Release?.VersionText=="0.5.0";
handler.Site=Site("0.6.0");result=await client.CheckAsync(UpdateSource.Auto,now);checks["higher_website_version_wins"]=result.Release?.VersionText=="0.6.0";
handler.SiteStatus=HttpStatusCode.ServiceUnavailable;result=await client.CheckAsync(UpdateSource.Auto,now);checks["website_failure_fallback"]=result.Release?.Source=="GitHub";
handler.SiteStatus=HttpStatusCode.OK;handler.GitStatus=HttpStatusCode.Forbidden;result=await client.CheckAsync(UpdateSource.Auto,now);checks["github_rate_limit_fallback"]=result.Release?.Source=="tianqistudio.com";
handler.GitStatus=HttpStatusCode.OK;handler.TimeoutSite=true;result=await client.CheckAsync(UpdateSource.Auto,now);checks["timeout_keeps_other_source"]=result.Release?.Source=="GitHub";handler.TimeoutSite=false;
handler.Site="<html>fallback page</html>";result=await client.CheckAsync(UpdateSource.Auto,now);checks["html_response_rejected"]=result.Release?.Source=="GitHub";
handler.Site=new string('x',262145);result=await client.CheckAsync(UpdateSource.Auto,now);checks["oversize_response_rejected"]=result.Release?.Source=="GitHub";
handler.Site="{}";handler.Git="[]";result=await client.CheckAsync(UpdateSource.Auto,now);checks["both_malformed_no_update"]=!result.AnySourceSucceeded&&result.Release==null;
handler.Site=Site("0.4.0");handler.Git=Git("0.4.0");result=await client.CheckAsync(UpdateSource.Auto,now);checks["same_version_no_update"]=result.Release?.Version==now&&!result.Message.StartsWith("发现新版 ");
handler.Site=Site("0.3.0");result=await client.CheckAsync(UpdateSource.Website,now);checks["no_downgrade_prompt"]=!result.Message.StartsWith("发现新版 ");
handler.Site=Site("0.4.0","b");result=await client.CheckAsync(UpdateSource.Auto,now);checks["same_version_digest_conflict"]=!result.AnySourceSucceeded;
foreach(var v in new[]{"0.4","0.5.0-beta","-1.0.0","1.2.3.4.5","99999999.0.0","1.0.0/evil"})
{try{UpdateClient.ParseVersion(v);checks["reject_version_"+v]=false;}catch{checks["reject_version_"+v]=true;}}
checks["three_four_part_equal"]=UpdateClient.ParseVersion("0.4.0")==UpdateClient.ParseVersion("0.4.0.0");
foreach(var bad in new[]{Site("0.5.0").Replace("https://github.com/","http://github.com/"),Site("0.5.0").Replace("tianqistudio/wordcatch","attacker/wordcatch"),Site("0.5.0").Replace("\"wordcatch\"","\"other-product\""),Site("0.5.0").Replace(new string('a',64),"bad")})
{try{UpdateClient.Parse(bad,false);checks["reject_manifest_"+checks.Count]=false;}catch{checks["reject_manifest_"+checks.Count]=true;}}
try{UpdateClient.Parse(Git("0.5.0").Replace("\"prerelease\":false","\"prerelease\":true"),true);checks["prerelease_rejected"]=false;}catch{checks["prerelease_rejected"]=true;}
try{UpdateClient.Parse(Git("0.5.0").Replace("\"size\":123","\"size\":0"),true);checks["missing_asset_rejected"]=false;}catch{checks["missing_asset_rejected"]=true;}
handler.Site=Site("0.5.0");handler.Git=Git("0.5.0");handler.Urls.Clear();await client.CheckAsync(UpdateSource.Website,now);checks["website_only_route"]=handler.Urls.SequenceEqual(new[]{UpdateClient.WebsiteUrl});handler.Urls.Clear();await client.CheckAsync(UpdateSource.GitHub,now);checks["github_only_route"]=handler.Urls.SequenceEqual(new[]{UpdateClient.GitHubUrl});
using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{await client.CheckAsync(UpdateSource.Auto,now,cancel.Token);checks["cancel_propagates"]=false;}catch(OperationCanceledException){checks["cancel_propagates"]=true;}}
var output=Path.GetFullPath(args.Length>0?args[0]:"artifacts/qa/v0.4.0");Directory.CreateDirectory(output);
var settings=new UpdateSettings(Path.Combine(output,"synthetic-update-preferences.json"));settings.Save(new(false,UpdateSource.Auto));var service=new UpdateService(settings,client);
int count=handler.Urls.Count;await service.CheckAutomaticAsync();checks["disabled_no_network"]=handler.Urls.Count==count;
settings.Save(new(true,UpdateSource.Auto,DateTimeOffset.UtcNow));await service.CheckAutomaticAsync();checks["six_hour_throttle"]=handler.Urls.Count==count;
await service.CheckAsync();checks["manual_bypasses_throttle"]=handler.Urls.Count>count;checks["new_version_available"]=service.HasUpdate;
var reread=new UpdateSettings(Path.Combine(output,"synthetic-update-preferences.json"));checks["preferences_roundtrip"]=reread.Options.AutoCheck&&reread.Options.LastCheckUtc!=null;
if(args.Contains("--live")) {
 var live=new UpdateClient();foreach(var source in new[]{UpdateSource.Website,UpdateSource.GitHub}){var r=await live.CheckAsync(source,now);checks["live_"+source]=r.AnySourceSucceeded&&r.Release?.Version==now;}
}
Console.WriteLine(JsonSerializer.Serialize(new{passed=checks.Values.All(x=>x),checks},new JsonSerializerOptions{WriteIndented=true}));return checks.Values.All(x=>x)?0:1;
sealed class Stub:HttpMessageHandler
{
 public string Site="",Git="";public bool TimeoutSite;public HttpStatusCode SiteStatus=HttpStatusCode.OK,GitStatus=HttpStatusCode.OK;public List<string> Urls=new();
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token)
 {token.ThrowIfCancellationRequested();Urls.Add(r.RequestUri!.AbsoluteUri);bool github=r.RequestUri.Host=="api.github.com";if(!github&&TimeoutSite)throw new TaskCanceledException();return Task.FromResult(new HttpResponseMessage(github?GitStatus:SiteStatus){Content=new StringContent(github?Git:Site)});}
}
