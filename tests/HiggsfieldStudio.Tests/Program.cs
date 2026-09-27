using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using HiggsfieldStudio.Core;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
void Throws(Action action, string name)
{
    try { action(); } catch { Check(true, name); return; }
    throw new Exception("FAIL: " + name);
}
Check(ApiCredentials.Parse("  demo-id:demo-secret\r\n") == ("demo-id", "demo-secret"), "combined credential paste accepts surrounding whitespace");
foreach (var invalidCredential in new[] { "", "demo-id", "demo-id:", ":demo-secret", "id:secret:extra", "id:sec ret" })
    Throws(() => ApiCredentials.Parse(invalidCredential), "incomplete or malformed combined credential is rejected");
var imageSettings = new GenerationSettings("gentle breeze", 5, "720p", "adaptive", false, false);
var body = imageSettings.Body(["https://cdn.example.test/image.png"]);
Check(body["image_url"] is not null && body["image_urls"] is null && body["generate_audio"]!.GetValue<bool>() == false, "image-to-video request preserves settings");
var references = imageSettings with { References = true };
Check(references.Body(["https://cdn.example.test/person.png", "https://cdn.example.test/place.png"])["image_urls"]!.AsArray().Count == 2, "reference mode carries both images");
Throws(() => (imageSettings with { Duration = 31 }).Body(["https://a.test/a.png"]), "reject invalid duration before network");
Throws(() => imageSettings.Body([]), "reject missing image");
var seedSettings = new GenerationSettings("gentle breeze", 5, "720p", "source", true, false, "seedance-2.5");
var seedBody = seedSettings.Body(["https://cdn.test/start.png"]);
Check(seedSettings.Endpoint == "bytedance/seedance-2.5/image-to-video" && seedBody["output_format"]!.ToString() == "mp4", "Seedance uses its own endpoint and MP4 output");
Check(!seedBody.ContainsKey("aspect_ratio") && !seedBody.ContainsKey("enable_thinking") && !seedBody.ContainsKey("bitrate_mode"), "Seedance image mode omits undocumented fields");
Throws(() => (seedSettings with { Resolution = "1080p" }).Body(["https://cdn.test/start.png"]), "Seedance follows detailed schema rather than conflicting overview resolution");
Throws(() => (seedSettings with { Duration = 3 }).Body(["https://cdn.test/start.png"]), "Seedance minimum is four seconds");
var seedRefs = seedSettings with { References = true, AspectRatio = "21:9" };
Check(seedRefs.Body(["https://cdn.test/a.png", "https://cdn.test/b.png"])["aspect_ratio"]!.ToString() == "21:9", "Seedance reference mode supports explicit aspect ratio");
var miniSettings = new GenerationSettings("gentle breeze", 15, "2K", "auto", false, false, "minimax-h3");
var miniBody = miniSettings.Body(["https://cdn.test/start.png"]);
Check(miniSettings.Endpoint == "minimax/h3/image-to-video" && miniBody["resolution"]!.ToString() == "2K", "MiniMax H3 uses 2K with correct endpoint");
Check(!miniBody.ContainsKey("generate_audio") && !miniBody.ContainsKey("sound") && !miniBody.ContainsKey("enable_thinking"), "MiniMax does not invent an audio control");
Check(!(miniSettings with { Audio = true }).Body(["https://cdn.test/start.png"]).ContainsKey("generate_audio"), "MiniMax audio intent does not invent an API switch");
Throws(() => (miniSettings with { Duration = 16 }).Body(["https://cdn.test/start.png"]), "MiniMax maximum is fifteen seconds");
var miniRefs = miniSettings with { References = true };
Check(miniRefs.Endpoint == "minimax/h3/reference-to-video" && miniRefs.Body(["https://cdn.test/a.png", "https://cdn.test/b.png"])["image_urls"]!.AsArray().Count == 2, "MiniMax reference images preserved");
Check(ModelCatalog.All.Count == 3, "catalog contains the three requested models");
Throws(() => ModelCatalog.Get("kling-3.0"), "Kling is excluded from generation");
foreach (var model in ModelCatalog.All)
{
    var greeting = SceneRecipe.Create(model.Id, 5, "turn", "");
    var greetingBody = greeting.Body(["https://cdn.test/person.png", "https://cdn.test/place.png"]);
    Check(greeting.Audio && greeting.References && greeting.AspectRatio == "16:9" && greeting.Resolution == (model.Id == "minimax-h3" ? "2K" : "720p"), model.Id + " fixed scene settings");
    Check(greeting.Prompt.Contains("今日は楽しかったよ！") && !greeting.Prompt.Contains("No speech"), model.Id + " default greeting without supplied dialogue");
    Check(!model.SupportsAudioToggle || greetingBody["generate_audio"]!.GetValue<bool>(), model.Id + " audio enabled when supported");
}
var silentWalk = SceneRecipe.Create("wan-3.0", 5, "walk", "");
foreach (var model in ModelCatalog.All)
{
    var turn = SceneRecipe.Create(model.Id, 6, "turn", " ");
    var custom = SceneRecipe.Create(model.Id, 6, "turn", "また明日ね！");
    Check(turn.Prompt.Contains("今日は楽しかったよ！") && !turn.Prompt.Contains("No speech"), model.Id + " blank dialogue uses action default");
    Check(custom.Prompt.Contains("また明日ね！") && !custom.Prompt.Contains("今日は楽しかったよ！"), model.Id + " custom dialogue replaces default entirely");
    var dance = SceneRecipe.Create(model.Id, 8, "dance", null);
    Check(dance.Audio && dance.Prompt.Contains("No speech") && dance.Prompt.Contains("instrumental dance-pop music") && !dance.Prompt.Contains("on-screen text, background music"), model.Id + " dance generates instrumental music without contradictory prohibition");
    var musicDance = SceneRecipe.Create(model.Id, 8, "dance", "ゆったりしたジャズ");
    Check(musicDance.Prompt.Contains("ゆったりしたジャズ") && musicDance.Prompt.Contains("No speech") && !musicDance.Prompt.Contains("speaks this exact") && !musicDance.Prompt.Contains("finish the speech") && !musicDance.Prompt.Contains("120 BPM"), model.Id + " music arrangement replaces default music without spoken dialogue");
}
Check(silentWalk.Prompt.Contains("No speech") && silentWalk.Prompt.Contains("environmental sounds"), "empty nonverbal action preserves environmental audio");
var spokenWalk = SceneRecipe.Create("wan-3.0", 8, "walk", "  こんにちは。  ");
Check(spokenWalk.Prompt.Contains("こんにちは。") && !spokenWalk.Prompt.Contains("No speech") && spokenWalk.Prompt.Contains("8-second"), "optional dialogue overrides silence and duration enters prompt");
Throws(() => SceneRecipe.Create("wan-3.0", 5, "missing", ""), "unknown action rejected");
Check(SceneRecipe.Actions.Select(a => a.Id).SequenceEqual(new[] { "turn", "dance", "lean", "walk", "invite" }), "five agreed actions in display order");
var invitation = SceneRecipe.Create("wan-3.0", 6, "invite", "");
Check(invitation.Prompt.Contains("一緒に行こう？") && !invitation.Prompt.Contains("No speech"), "invitation default dialogue is supplied");
Check(new Job { Endpoint = "alibaba/wan-3.0/image-to-video" }.ModelName == "Wan 3.0", "older jobs infer model name from endpoint");
foreach (var settings in new[] { imageSettings, seedSettings, miniSettings, references, seedRefs, miniRefs })
{
    var files = settings.References ? new[] { "https://cdn.test/a.png", "https://cdn.test/b.png" } : new[] { "https://cdn.test/a.png" };
    var requestBody = settings.Body(files);
    var routes = new FakeHandler();
    routes.Enqueue(req => { Check(req.RequestUri!.AbsolutePath == "/estimate/" + settings.Endpoint, settings.ModelId + " estimate route / " + settings.References); return Json("{\"usd\":\"0.5\"}"); });
    routes.Enqueue(req => { Check(req.RequestUri!.AbsolutePath == "/" + settings.Endpoint, settings.ModelId + " generation route / " + settings.References); return Json("{\"status\":\"queued\"}"); });
    using var routedApi = new ApiClient("test-id", "test-secret", routes);
    await routedApi.EstimateAsync(settings.Endpoint, requestBody, default);
    await routedApi.SubmitAsync(settings.Endpoint, requestBody, default);
}
Throws(() => ApiClient.RequireApi("https://attacker.example/status"), "credentials restricted to official API host");
Throws(() => ApiClient.RequireApi("https://api.higgsfield.ai:444/status"), "credentials restricted to HTTPS default port");
var platformStatus = "https://platform.higgsfield.ai/requests/11111111-2222-4333-8444-555555555555/status";
Check(ApiClient.RequireStatus(platformStatus).Host == "platform.higgsfield.ai", "accept real API platform status URL");
Throws(() => ApiClient.RequireApi(platformStatus), "platform is not allowed for generation POSTs");
foreach (var invalidStatus in new[] { platformStatus.Replace("platform.higgsfield.ai", "platform.higgsfield.ai.attacker.test"), platformStatus.Replace("https:", "http:"), platformStatus.Replace("/status", "/cancel"), platformStatus.Replace(".ai/", ".ai:444/"), platformStatus + "?redirect=elsewhere" })
    Throws(() => ApiClient.RequireStatus(invalidStatus), "reject untrusted status destination");
var platformHandler = new FakeHandler();
platformHandler.Enqueue(request =>
{
    Check(request.Method == HttpMethod.Get && request.RequestUri!.ToString() == platformStatus, "poll returned platform URL with GET");
    Check(request.Headers.Authorization?.Scheme == "Key", "status request includes official API authentication");
    return Json("{\"status\":\"completed\",\"video\":{\"url\":\"https://cdn.test/recovered.mp4\"}}");
});
using (var platformApi = new ApiClient("mock-id", "mock-secret", platformHandler))
{
    var recoveredJob = new Job { Status = "queued", StatusUrl = platformStatus };
    await platformApi.PollAsync(recoveredJob, _ => { }, default);
    Check(recoveredJob.Status == "completed" && recoveredJob.VideoUrl.EndsWith("recovered.mp4") && platformHandler.Count == 1, "recover accepted job without resubmission");
}
Throws(() => ApiClient.RequireHttps("http://example.test/file"), "reject insecure media URL");
Throws(() => Estimate.Parse(new JsonObject { ["credits"] = 5 }), "missing USD cannot authorize spending");
Throws(() => Estimate.Parse(new JsonObject { ["usd"] = "-1" }), "negative USD cannot authorize spending");
var price = Estimate.Parse(new JsonObject { ["usd"] = "0.25", ["credits"] = "4" });
Check(price.Usd == .25m, "parse decimal estimate");
Throws(() => price.CheckBudget(.1m), "budget enforced");
var safe = ApiClient.SafeJson(new JsonObject { ["secret"] = "hidden-secret", ["video"] = new JsonObject { ["url"] = "https://cdn.test/a?token=private" }, ["request_id"] = "real-job-123" });
Check(!safe.Contains("hidden-secret") && !safe.Contains("private") && safe.Contains("real-job-123"), "trace hides credentials and URLs but preserves real request ID");

var handler = new FakeHandler();
handler.Enqueue(_ => Json("{\"public_url\":\"https://cdn.test/input.png\",\"upload_url\":\"https://storage.test/upload?signature=abc\",\"upload_headers\":{\"Content-Type\":\"image/png\",\"x-amz-tagging\":\"retention=temporary\"}}"));
handler.Enqueue(request =>
{
    Check(request.Headers.Authorization is null, "upload never receives API credential");
    Check(request.Headers.GetValues("x-amz-tagging").Single() == "retention=temporary", "upload uses all returned storage headers");
    Check(request.Content!.Headers.ContentType!.MediaType == "image/png", "upload MIME matches presign");
    return new HttpResponseMessage(HttpStatusCode.OK);
});
handler.Enqueue(request =>
{
    Check(request.RequestUri!.AbsolutePath == "/estimate/alibaba/wan-3.0/image-to-video", "correct estimate endpoint");
    Check(request.Headers.Authorization?.Scheme == "Key", "official API receives Key auth");
    return Json("{\"usd\":\"0.25\",\"credits\":\"4\"}");
});
handler.Enqueue(_ => Json("{\"status\":\"queued\",\"request_id\":\"r-123\",\"status_url\":\"https://api.higgsfield.ai/requests/r-123/status\"}"));
handler.Enqueue(_ => Json("{\"status\":\"completed\",\"request_id\":\"r-123\",\"video\":{\"url\":\"https://cdn.test/result.mp4\"}}"));
handler.Enqueue(request =>
{
    Check(request.Headers.Authorization is null, "download never receives API credential");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) };
});
var temp = Path.Combine(Path.GetTempPath(), "higgsfield-studio-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var input = Path.Combine(temp, "input.png"); await File.WriteAllBytesAsync(input, [137, 80, 78, 71]);
    using var api = new ApiClient("test-id-only", "test-secret-only", handler);
    var trace = new StringBuilder(); api.Trace += x => trace.AppendLine(x);
    var url = await api.UploadAsync(input, default);
    var payload = imageSettings.Body([url]);
    var quote = Estimate.Parse(await api.EstimateAsync(imageSettings.Endpoint, payload, default));
    quote.CheckBudget(1m);
    var store = new JobStore(Path.Combine(temp, "jobs"));
    var job = new Job { EstimatedUsd = quote.Usd };
    job.Apply(await api.SubmitAsync(imageSettings.Endpoint, payload, default)); store.Save(job);
    Check(store.Load().Single().RequestId == "r-123", "accepted job persists for restart");
    await api.PollAsync(job, store.Save, default);
    Check(job.Status == "completed" && job.VideoUrl.EndsWith("result.mp4"), "queued job resolves to completed video");
    var output = Path.Combine(temp, "result.mp4"); await api.DownloadAsync(job.VideoUrl, output, default);
    Check(new FileInfo(output).Length == 4 && !File.Exists(output + ".part"), "download saved atomically");
    Check(!trace.ToString().Contains("test-secret-only") && !trace.ToString().Contains("signature=abc"), "end-to-end trace contains no credentials or presigned URL");
    Check(handler.Count == 6, "one upload, one quote, one submit, one poll and one download sequence");

    var reusedUrl = await api.UploadAsync(input, default);
    Check(reusedUrl == url && handler.Count == 6, "same image is reused without presign or upload requests");
    var duplicate = Path.Combine(temp, "duplicate.png"); File.Copy(input, duplicate);
    Check(await api.UploadAsync(duplicate, default) == url && handler.Count == 6, "identical image under another filename reuses upload");
    Check(trace.ToString().Contains("アップロード済み画像を再利用") && !trace.ToString().Contains(url), "reuse is visible in trace without revealing URL");
    var originalTime = File.GetLastWriteTimeUtc(input);
    await File.WriteAllBytesAsync(input, [137, 80, 78, 72]); File.SetLastWriteTimeUtc(input, originalTime);
    handler.Enqueue(_ => Json("{\"public_url\":\"https://cdn.test/changed.png\",\"upload_url\":\"https://storage.test/changed\"}"));
    handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK));
    Check(await api.UploadAsync(input, default) == "https://cdn.test/changed.png" && handler.Count == 8, "changed bytes reupload even with same filename size and timestamp");
    var newSessionHandler = new FakeHandler();
    newSessionHandler.Enqueue(_ => Json("{\"public_url\":\"https://cdn.test/new-session.png\",\"upload_url\":\"https://storage.test/new-session\"}"));
    newSessionHandler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK));
    using var newSessionApi = new ApiClient("test-id-only", "test-secret-only", newSessionHandler);
    Check(await newSessionApi.UploadAsync(input, default) == "https://cdn.test/new-session.png" && newSessionHandler.Count == 2, "new session uploads again instead of inheriting cached URLs");
    var retryUploadHandler = new FakeHandler();
    for (var attempt = 0; attempt < 2; attempt++)
    {
        retryUploadHandler.Enqueue(_ => Json("{\"public_url\":\"https://cdn.test/retry.png\",\"upload_url\":\"https://storage.test/retry\"}"));
        var status = attempt == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
        retryUploadHandler.Enqueue(_ => new HttpResponseMessage(status));
    }
    using var retryUploadApi = new ApiClient("id", "secret", retryUploadHandler);
    try { await retryUploadApi.UploadAsync(input, default); throw new Exception("Expected upload failure"); }
    catch (InvalidOperationException) { }
    Check(await retryUploadApi.UploadAsync(input, default) == "https://cdn.test/retry.png" && retryUploadHandler.Count == 4, "failed uploads are not cached and can be retried explicitly");

    var failure = new FakeHandler(); failure.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    using var failApi = new ApiClient("id", "secret", failure);
    try { await failApi.SubmitAsync(imageSettings.Endpoint, payload, default); throw new Exception("Expected rejection"); }
    catch (ApiException) { Check(failure.Count == 1, "generation POST is never automatically retried"); }
    var failedHandler = new FakeHandler(); failedHandler.Enqueue(_ => Json("{\"status\":\"failed\"}"));
    using var failedApi = new ApiClient("id", "secret", failedHandler);
    var failedJob = new Job { Status = "queued", StatusUrl = "https://api.higgsfield.ai/requests/f/status" };
    await failedApi.PollAsync(failedJob, _ => { }, default);
    Check(failedJob.Status == "failed" && failedHandler.Count == 1, "failed is terminal; no extra polling or generation");
}
finally { Directory.Delete(temp, true); }
Console.WriteLine($"All {passed} checks passed. No live API calls or credits used.");

static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> queue = new();
    public int Count { get; private set; }
    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> callback) => queue.Enqueue(callback);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Count++;
        if (queue.Count == 0) throw new Exception("Unexpected network call");
        return Task.FromResult(queue.Dequeue()(request));
    }
}
