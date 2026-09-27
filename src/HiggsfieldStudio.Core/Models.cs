using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HiggsfieldStudio.Core;

public sealed record GenerationSettings(string Prompt, int Duration, string Resolution, string AspectRatio, bool Audio, bool References, string ModelId = "wan-3.0")
{
    public VideoModel Model => ModelCatalog.Get(ModelId);
    public string Endpoint => Model.Endpoint(References);
    public JsonObject Body(IReadOnlyList<string> urls)
    {
        if (string.IsNullOrWhiteSpace(Prompt)) throw new ArgumentException("動きの指示を入力してください。");
        _ = Endpoint; // Reject unsupported modes before upload/estimate.
        if (Duration < Model.MinSeconds || Duration > Model.MaxSeconds) throw new ArgumentException($"{Model.Name}の秒数は{Model.MinSeconds}〜{Model.MaxSeconds}秒です。");
        if (!Model.Resolutions.Contains(Resolution)) throw new ArgumentException($"{Model.Name}に対応していない解像度です。");
        if (!Model.AspectRatios(References).Contains(AspectRatio)) throw new ArgumentException($"{Model.Name}に対応していない縦横比です。");
        if (urls.Count == 0 || urls.Count > 2 || (!References && urls.Count != 1)) throw new ArgumentException("開始画像は1枚、参照画像は最大2枚です。");
        foreach (var url in urls) ApiClient.RequireHttps(url);
        var body = new JsonObject { ["prompt"] = Prompt.Trim(), ["duration"] = Duration };
        switch (ModelId)
        {
            case "wan-3.0":
                body["resolution"] = Resolution; body["aspect_ratio"] = AspectRatio;
                body["generate_audio"] = Audio; body["enable_thinking"] = false;
                break;
            case "seedance-2.5":
                body["resolution"] = Resolution; body["output_format"] = "mp4";
                body["generate_audio"] = Audio;
                if (References) body["aspect_ratio"] = AspectRatio;
                break;
            case "minimax-h3":
                body["resolution"] = Resolution; body["aspect_ratio"] = AspectRatio;
                body["aigc_watermark"] = false;
                break;
        }
        if (References) body["image_urls"] = new JsonArray(urls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray());
        else body["image_url"] = urls[0];
        return body;
    }
}

public sealed record Estimate(decimal Usd, string Credits)
{
    public static Estimate Parse(JsonObject json)
    {
        if (!decimal.TryParse(json["usd"]?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var usd) || usd < 0)
            throw new InvalidDataException("APIの見積額を読み取れません。生成は開始しません。");
        return new Estimate(usd, json["credits"]?.ToString() ?? "—");
    }
    public void CheckBudget(decimal limit)
    {
        if (limit <= 0 || Usd > limit) throw new InvalidOperationException($"見積額 ${Usd:0.####} が1回の上限 ${limit:0.##} を超えています。");
    }
}

public sealed class Job
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Created { get; set; } = DateTime.Now;
    public string Endpoint { get; set; } = "";
    public string ModelName => ModelCatalog.FromEndpoint(Endpoint)?.Name ?? "モデル不明";
    public int Duration { get; set; }
    public string Resolution { get; set; } = "";
    public string AspectRatio { get; set; } = "";
    public bool Audio { get; set; }
    public string ReferencePath { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string StatusUrl { get; set; } = "";
    public string Status { get; set; } = "submitting";
    public decimal EstimatedUsd { get; set; }
    public string Prompt { get; set; } = "";
    public string InputPath { get; set; } = "";
    public string VideoUrl { get; set; } = "";
    public string LocalVideo { get; set; } = "";
    public string Display => $"{Created:MM/dd HH:mm}  ·  {ModelName}  ·  {Status}";
    public bool Terminal => Status is "completed" or "failed" or "nsfw" or "canceled";
    public void Apply(JsonObject json)
    {
        RequestId = json["request_id"]?.ToString() ?? RequestId;
        StatusUrl = json["status_url"]?.ToString() ?? StatusUrl;
        Status = json["status"]?.ToString() ?? Status;
        VideoUrl = (json["video"] as JsonObject)?["url"]?.ToString() ?? VideoUrl;
    }
}

public sealed class JobStore(string directory)
{
    public string DirectoryPath => directory;
    public void Save(Job job)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, job.Id + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(job, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
    public List<Job> Load()
    {
        if (!Directory.Exists(directory)) return [];
        var jobs = new List<Job>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try { if (JsonSerializer.Deserialize<Job>(File.ReadAllText(path)) is { } j) jobs.Add(j); }
            catch (JsonException) { }
        }
        return jobs.OrderByDescending(j => j.Created).ToList();
    }
}
