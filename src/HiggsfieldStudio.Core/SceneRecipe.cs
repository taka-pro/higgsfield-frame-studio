namespace HiggsfieldStudio.Core;

public sealed record SceneAction(string Id, string Name, string Direction, bool SpeaksNaturally, string? DefaultDialogue = null)
{
    public string DefaultComment => Id == "dance" ? "明るいダンスポップ（約120 BPM・歌なし）"
        : DefaultDialogue is not null ? $"「{DefaultDialogue}」" : "セリフなし（環境音あり）";
    public string ArrangementLabel => Id == "dance" ? "音楽アレンジ（任意）" : "セリフアレンジ（任意）";
    public string DialogueHint => Id == "dance" ? "空欄ならデフォルトの音楽。例：ゆったりしたジャズ、軽快なファンク。セリフは入りません。"
        : DefaultDialogue is not null
        ? $"空欄なら「{DefaultDialogue}」。入力すると上書きします。"
        : SpeaksNaturally ? "空欄なら自然な日本語の挨拶。入力するとそのセリフを話します。"
        : "空欄なら発話なし・環境音あり。入力するとそのセリフを話します。";
}

public static class SceneRecipe
{
    public static IReadOnlyList<SceneAction> Actions { get; } = new SceneAction[]
    {
        new("turn", "振り返って挨拶", "Start in a waist-up three-quarter rear view, face partially visible in profile. Keep both feet planted and turn the head and shoulders smoothly toward the camera. Meet the viewer's eyes, smile warmly, then speak while holding a clear three-quarter front view. Finish with a small wave. No full-body spin or walking.", false, "今日は楽しかったよ！"),
        new("dance", "音楽に合わせてダンス", "Start and remain in a knees-up shot with both hands visible. Perform a compact, upbeat social-media-style dance in place: two gentle side-to-side weight shifts with coordinated shoulder bounces and simple alternating hand gestures, then finish with a playful pose. Keep feet within the same small area. No spins, jumps, acrobatics or costume changes.", false),
        new("lean", "前かがみでコメント", "Start in a waist-up medium shot at eye level. Keep both feet planted. With warm, affectionate familiarity, lean the upper body gently toward the lens, tilt the head slightly and make playful eye contact, as if looking closely at someone dear. End in a chest-up view with a soft smile. Do not walk forward or touch the lens. Keep the entire head in frame. Speak while holding the closer pose.", false, "ねえ、どうしたの？"),
        new("walk", "近づいて笑いかける", "Start in a waist-up shot. Take two small relaxed steps toward the fixed camera, stop naturally in a chest-up view and smile directly at the viewer. Keep the entire head in frame. Speak after stopping if dialogue is requested.", false),
        new("invite", "手を差し伸べて誘う", "Start and remain in an eye-level waist-up shot. Keep both feet planted. Make warm eye contact, slowly extend one open hand toward the viewer as an invitation, keeping the hand below the face and a comfortable distance from the lens. Tilt the head slightly, speak, and hold a gentle smile with the hand offered. Keep the entire hand and head visible. No walking, touching the camera, grabbing or extreme foreshortening.", false, "一緒に行こう？")
    };

    public static GenerationSettings Create(string modelId, int seconds, string actionId, string? dialogue)
    {
        var model = ModelCatalog.Get(modelId);
        var action = Actions.FirstOrDefault(a => a.Id == actionId) ?? throw new ArgumentException("行動を選択してください。");
        var line = action.Id == "dance" ? "" : string.IsNullOrWhiteSpace(dialogue) ? action.DefaultDialogue ?? "" : dialogue.Trim();
        var speech = line.Length > 0
            ? "The character speaks this exact Japanese dialogue naturally, with synchronized lip movement: " + System.Text.Json.JsonSerializer.Serialize(line, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            : action.SpeaksNaturally
                ? "The character says a brief, friendly greeting in Japanese. Choose natural wording appropriate to the scene and available duration."
                : "No speech or narration; include natural environmental sounds and sounds of the action.";
        if (action.Id == "dance")
        {
            var music = "Audio: Generate original cheerful instrumental dance-pop music, approximately 120 BPM, with a clear steady drum beat, light bass and bright synth notes. Keep the music clearly audible throughout the shot. Synchronize the shoulder bounces, side-to-side weight shifts and hand gestures to this beat. ";
            if (!string.IsNullOrWhiteSpace(dialogue))
                music = "Audio: Generate original instrumental music using this musical style direction: "
                    + System.Text.Json.JsonSerializer.Serialize(dialogue.Trim(), new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
                    + ". This describes the music, not words to say. Keep the music clearly audible throughout the shot. Synchronize the shoulder bounces, side-to-side weight shifts and hand gestures to its beat. ";
            speech = music + "No speech, singing, lyrics, humming or vocal samples. Subtle environmental sounds may sit quietly beneath the instrumental music.";
        }
        var refs = modelId == "seedance-2.5" ? "Use @Image 1 for the character and @Image 2 for the location. "
            : modelId == "minimax-h3" ? "Use Picture 1 for the character and Picture 2 for the location. "
            : "Use Image 1 for the character and Image 2 for the location. ";
        var prompt = refs
            + "Preserve the exact character identity from the first reference throughout: facial structure, eye shape and color, hairstyle, hair color, apparent age, body proportions, outfit design and colors, fabric patterns, accessories and footwear wherever visible. No face redesign, beautification, hairstyle changes, outfit replacement, missing accessories or new garments. The first reference defines appearance, not the camera framing. "
            + "Preserve the character reference's original visual medium and style. If it is anime or illustration, keep its drawn face, linework, shading and stylized proportions while animating; do not turn it into a live-action person or a different 3D style. If photographic, preserve its photographic appearance. Match placement and lighting to the second reference without changing the character's design or copying a person from the background. "
            + action.Direction + " " + speech
            + $" Create one continuous {seconds}-second shot with natural sound. Adapt the motion timing to the available duration, "
            + (line.Length > 0 || action.SpeaksNaturally ? "finish the speech before the end, and " : "")
            + "hold the final expression briefly. Keep the camera fixed: no zoom, dolly or cuts. Only the character performs the specified motion. Maintain coherent anatomy and temporal consistency. "
            + (action.Id == "dance" ? "No subtitles, on-screen text or additional voices." : "No subtitles, on-screen text, background music or additional voices.");
        // Audio expresses the user's intent. Models without a switch receive the sound direction in the prompt only.
        return new(prompt, seconds, modelId == "minimax-h3" ? "2K" : "720p", "16:9", true, true, model.Id);
    }
}
