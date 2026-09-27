namespace HiggsfieldStudio.Core;

// Checked against each model's Request parameters table, 2026-09-27.
// Shared authentication does not imply shared request parameters.
public sealed record VideoModel(string Id, string Name, string Prefix, int MinSeconds, int MaxSeconds,
    string[] Resolutions, bool SupportsReferences, bool SupportsAudioToggle, string Hint)
{
    public string Endpoint(bool references)
    {
        if (references && !SupportsReferences) throw new ArgumentException($"{Name}ではこのアプリの参照画像モードは利用できません。");
        return Prefix + (references ? "/reference-to-video" : "/image-to-video");
    }
    public string[] AspectRatios(bool references) => Id switch
    {
        "seedance-2.5" when !references => ["source"],
        "seedance-2.5" => ["16:9", "4:3", "1:1", "3:4", "9:16", "21:9"],
        "minimax-h3" => ["auto", "adaptive", "21:9", "16:9", "4:3", "1:1", "3:4", "9:16"],
        _ => ["adaptive", "16:9", "4:3", "1:1", "3:4", "9:16"]
    };
}

public static class ModelCatalog
{
    public static IReadOnlyList<VideoModel> All { get; } = new VideoModel[]
    {
        new("seedance-2.5", "Seedance 2.5", "bytedance/seedance-2.5", 4, 30,
            ["480p", "720p"], true, true, "4〜30秒 / 480p・720p。開始画像モードは元画像の比率を使用します。"),
        new("minimax-h3", "MiniMax H3", "minimax/h3", 5, 15,
            ["2K"], true, false, "5〜15秒 / 2K。公式にPreview表記あり。音声ON/OFFの指定項目はありません。"),
        new("wan-3.0", "Wan 3.0", "alibaba/wan-3.0", 2, 30,
            ["480p", "720p", "1080p"], true, true, "2〜30秒 / 480p・720p・1080p。人物と背景の参照入力にも対応します。")
    };
    public static VideoModel Get(string id) => All.FirstOrDefault(m => m.Id == id) ?? throw new ArgumentException("未対応のモデルです。");
    public static VideoModel? FromEndpoint(string endpoint) => All.FirstOrDefault(m => endpoint.StartsWith(m.Prefix + "/", StringComparison.Ordinal));
}
