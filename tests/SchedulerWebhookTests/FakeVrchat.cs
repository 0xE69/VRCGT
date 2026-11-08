using System.Net;
using System.Net.Http;
using Newtonsoft.Json.Linq;
public sealed class FakeVrchat : HttpMessageHandler
{
 public string Group = "", World = "", Instance = ""; public bool Failed; public int Calls;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
 {
    ct.ThrowIfCancellationRequested(); Calls++;
    if (request.Method != HttpMethod.Get || request.RequestUri!.Host != "offline.invalid") throw new Exception("Real/mutating request forbidden");
    if (Failed) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    string body;
    if (request.RequestUri.AbsolutePath == "/groups/" + Group + "/instances") body = new JArray(new JObject { ["location"] = World + ":" + Instance, ["instanceId"] = Instance, ["memberCount"] = 1 }).ToString();
    else if (Uri.UnescapeDataString(request.RequestUri.AbsolutePath) == "/instances/" + World + ":" + Instance)
       body = new JObject { ["location"] = World + ":" + Instance, ["worldId"] = World, ["ownerId"] = Group, ["type"] = "group", ["groupAccessType"] = "public", ["accessType"] = "groupPublic", ["active"] = true, ["closedAt"] = JValue.CreateNull(), ["roleRestricted"] = false }.ToString();
    else throw new Exception("Unexpected VRChat endpoint: " + request.RequestUri.AbsolutePath);
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
 }
}
