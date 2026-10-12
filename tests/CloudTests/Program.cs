using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using WordLens;

var checks = new Dictionary<string, bool>();
var transport = new Stub();
var client = new AzureTranslator(transport);
var result = await client.TranslateAsync("A small test.", "fake-test-key", "eastasia");
checks["translate"] = result == "一段测试。";
checks["fixed_https_endpoint"] = transport.Uri == "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to=zh-Hans";
checks["key_in_header_only"] = transport.Key == "fake-test-key" && !transport.Body.Contains("fake-test-key");
checks["text_only_no_image"] = transport.Body == "[{\"Text\":\"A small test.\"}]";
checks["region_header"] = transport.Region == "eastasia";
foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
{
    transport.Status = status;
    try { await client.TranslateAsync("Hello", "fake-test-key", ""); checks[$"error_{(int)status}"] = false; }
    catch (InvalidOperationException e) { checks[$"error_{(int)status}"] = !e.Message.Contains("fake-test-key"); }
}
transport.Status = HttpStatusCode.OK;
transport.Malformed = true;
try { await client.TranslateAsync("Hello", "fake-test-key", ""); checks["malformed_json"] = false; }
catch (InvalidOperationException) { checks["malformed_json"] = true; }
transport.Malformed = false;
transport.Timeout = true;
try { await client.TranslateAsync("Hello", "fake-test-key", ""); checks["timeout_message"] = false; }
catch (InvalidOperationException e) { checks["timeout_message"] = e.Message.Contains("超时"); }
transport.Timeout = false;
int before = transport.Calls;
try { await client.TranslateAsync("Hello", "", ""); } catch (InvalidOperationException) { }
checks["missing_key_no_request"] = before == transport.Calls;
try { await client.TranslateAsync(new string('a',2001), "fake-test-key", ""); } catch (ArgumentException) { }
checks["long_text_no_request"] = before == transport.Calls;
using (var cancel = new CancellationTokenSource())
{
    cancel.Cancel();
    try { await client.TranslateAsync("Hello", "fake-test-key", "", cancel.Token); checks["cancellation"] = false; }
    catch (OperationCanceledException) { checks["cancellation"] = true; }
}
var encrypted = System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes("synthetic-key"), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
checks["dpapi_roundtrip"] = Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(encrypted,null,System.Security.Cryptography.DataProtectionScope.CurrentUser)) == "synthetic-key";
checks["dpapi_not_plaintext"] = !Encoding.UTF8.GetString(encrypted).Contains("synthetic-key");
var disabled = new TranslationService(new CloudSettings(Path.Combine(AppContext.BaseDirectory,"absent-cloud.json")), client);
before = transport.Calls;
try { await disabled.TranslateAsync("Hello"); } catch (InvalidOperationException) { }
checks["disabled_no_request"] = before == transport.Calls;
Console.WriteLine(JsonSerializer.Serialize(new { passed=checks.Values.All(x=>x), checks },new JsonSerializerOptions { WriteIndented=true }));
return checks.Values.All(x=>x) ? 0 : 1;

sealed class Stub : HttpMessageHandler
{
    public string Uri="", Key="", Region="", Body="";
    public HttpStatusCode Status=HttpStatusCode.OK;
    public bool Malformed;
    public bool Timeout;
    public int Calls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Calls++;
        if(Timeout) throw new TaskCanceledException();
        Uri=request.RequestUri!.ToString();
        Key=string.Join("",request.Headers.GetValues("Ocp-Apim-Subscription-Key"));
        Region=request.Headers.TryGetValues("Ocp-Apim-Subscription-Region",out var values)?string.Join("",values):"";
        Body=await request.Content!.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(Status) {Content=new StringContent(Malformed?"not-json":"[{\"translations\":[{\"text\":\"一段测试。\",\"to\":\"zh-Hans\"}]}]")};
    }
}
