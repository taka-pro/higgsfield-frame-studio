using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using HiggsfieldStudio.Core;
using Microsoft.Win32;

namespace HiggsfieldStudio;

public partial class MainWindow : Window
{
    private sealed record Asset(string Path, string Name, BitmapSource Thumbnail);
    private readonly List<Asset> assets = [];
    private readonly JobStore jobs;
    private readonly HashSet<string> sessionJobIds = [];
    private readonly Dictionary<string, string> arrangements = [];
    private string currentActionId = "turn";
    private string keyId = "", secret = "", sourcePath = "", referencePath = "", videoPath = "", videoModelName = "";
    private bool ready, busy;
    private ApiClient? client;
    private CancellationTokenSource? operation;
    private bool submissionUncertain;
    private Job? selectedJob;

    public MainWindow()
    {
        jobs = new JobStore(Path.Combine(App.DataPath, "jobs"));
        InitializeComponent();
        ModelBox.ItemsSource = ModelCatalog.All;
        ModelBox.SelectedItem = ModelCatalog.Get("wan-3.0");
        ActionBox.ItemsSource = SceneRecipe.Actions;
        ActionBox.SelectedIndex = 0;
        ready = true;
        ConfigureModelOptions();
        LoadCredentials();
        var refs = Path.Combine(App.Workspace, "ref");
        if (Directory.Exists(refs))
            foreach (var path in Directory.EnumerateFiles(refs).Where(IsImage)) TryAddAsset(path);
        AssetList.ItemsSource = assets.Where(a => !a.Name.StartsWith("place", StringComparison.OrdinalIgnoreCase)).ToList();
        BackgroundList.ItemsSource = assets.Where(a => a.Name.StartsWith("place", StringComparison.OrdinalIgnoreCase)).ToList();
        if (AssetList.Items.Count > 0) AssetList.SelectedIndex = 0;
        if (BackgroundList.Items.Count > 0) BackgroundList.SelectedIndex = 0;
        RefreshHistory();
        UpdatePreviewRequest();
    }
    private static bool IsImage(string p) => new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(p).ToLowerInvariant());
    private static BitmapImage Bitmap(string path, int width)
    {
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.DecodePixelWidth = width; image.EndInit(); image.Freeze();
        return image;
    }
    private void TryAddAsset(string path)
    {
        if (assets.Any(a => a.Path == path)) return;
        try { assets.Add(new Asset(path, Path.GetFileNameWithoutExtension(path), Bitmap(path, 400))); }
        catch { StatusText.Text = "読み込めない画像をスキップしました。PNG/JPEGを確認してください。"; }
    }
    private void LoadCredentials()
    {
        keyId = Environment.GetEnvironmentVariable("HF_API_KEY_ID") ?? "";
        secret = Environment.GetEnvironmentVariable("HF_API_KEY_SECRET") ?? "";
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret))
        {
            var combined = Environment.GetEnvironmentVariable("HF_CREDENTIALS") ?? Environment.GetEnvironmentVariable("HF_KEY");
            if (combined?.Split(':', 2) is { Length: 2 } parts) { keyId = parts[0]; secret = parts[1]; }
            else try { (keyId, secret) = CredentialVault.Load(); } catch { StatusText.Text = "保存されたキーを復号できません。API設定から再入力してください。"; }
        }
        RecreateClient();
    }
    private void RecreateClient()
    {
        client?.Dispose(); client = null;
        try
        {
            if (keyId.Length > 0 && secret.Length > 0) { client = new ApiClient(keyId, secret); client.Trace += AppendTrace; }
        }
        catch { keyId = secret = ""; }
        KeyStatus.Text = client is null ? "APIキー 未設定" : "APIキー 設定済み";
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var window = new SettingsWindow(keyId, secret) { Owner = this };
        if (window.ShowDialog() != true) return;
        try
        {
            if (window.Remember) CredentialVault.Save(window.KeyId, window.Secret); else CredentialVault.Delete();
            keyId = window.KeyId; secret = window.Secret; RecreateClient(); RefreshInputState();
            StatusText.Text = "APIキーを設定しました。接続は生成時に確認します。";
        }
        catch { MessageBox.Show(this, "キーの保存に失敗しました。保存先の権限を確認してください。", "設定エラー"); }
    }
    private void AddImage_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "画像|*.png;*.jpg;*.jpeg", Multiselect = true, InitialDirectory = Path.Combine(App.Workspace, "ref") };
        if (dialog.ShowDialog() != true) return;
        foreach (var path in dialog.FileNames) TryAddAsset(path);
        AssetList.ItemsSource = AssetList.Items.Cast<Asset>().Concat(assets.Where(a => dialog.FileNames.Contains(a.Path))).DistinctBy(a => a.Path).ToList();
        AssetList.SelectedItem = assets.LastOrDefault(a => a.Path == dialog.FileNames.Last());
    }
    private void AssetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || busy || AssetList.SelectedItem is not Asset asset) return;
        try { sourcePath = asset.Path; ShowSource(); RefreshInputState(); }
        catch { StatusText.Text = "画像を表示できません。ファイルを確認してください。"; }
    }
    private void Reference_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "背景画像|*.png;*.jpg;*.jpeg", InitialDirectory = Path.Combine(App.Workspace, "ref") };
        if (dialog.ShowDialog() != true) return;
        TryAddAsset(dialog.FileName);
        BackgroundList.ItemsSource = BackgroundList.Items.Cast<Asset>().Concat(assets.Where(a => a.Path == dialog.FileName)).DistinctBy(a => a.Path).ToList();
        BackgroundList.SelectedItem = assets.FirstOrDefault(a => a.Path == dialog.FileName);
    }
    private void Background_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || busy || BackgroundList.SelectedItem is not Asset asset) return;
        referencePath = asset.Path;
        ShowSource(); RefreshInputState();
    }
    private VideoModel SelectedModel => (VideoModel)ModelBox.SelectedItem;
    private void Model_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        ConfigureModelOptions();
        StatusText.Text = $"{SelectedModel.Name}を選択しました。素材と動きを選んで生成できます。";
    }
    private static string? CurrentChoice(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    private static void FillOptions(ComboBox box, IEnumerable<string> choices, string? preferred)
    {
        var values = choices.ToArray();
        box.Items.Clear();
        foreach (var value in values)
            box.Items.Add(new ComboBoxItem { Tag = value, Content = value switch { "source" => "元画像に従う", "standard" => "Standard（指定なし）", _ => value } });
        box.SelectedIndex = Math.Max(0, Array.IndexOf(values, preferred));
    }
    private void ConfigureModelOptions()
    {
        var model = SelectedModel;
        var seconds = CurrentChoice(DurationBox) ?? "6";
        ready = false;
        try
        {
            var durations = Enumerable.Range(model.MinSeconds, model.MaxSeconds - model.MinSeconds + 1).Select(n => n.ToString()).ToArray();
            FillOptions(DurationBox, durations, durations.Contains(seconds) ? seconds : "5");
            ModelBadge.Text = model.Name;
            ModelHint.Visibility = model.Id == "minimax-h3" ? Visibility.Visible : Visibility.Collapsed;
            ModelHint.Text = model.Id == "minimax-h3" ? "MiniMax H3 · 2K固定。音声はプロンプトで指示します。" : "キャラと背景から、音声付きのワンシーンを生成します。";
            FixedSettingsLabel.Text = (model.Id == "minimax-h3" ? "2K固定" : "720p固定") + " · 横長16:9 · 音声あり";
        }
        finally { ready = true; }
        RefreshInputState();
    }
    private void Input_Changed(object sender, TextChangedEventArgs e) { if (ready) RefreshInputState(); }
    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        if (ActionBox.SelectedItem is SceneAction action && action.Id != currentActionId)
        {
            arrangements[currentActionId] = DialogueBox.Text;
            currentActionId = action.Id;
            ready = false;
            DialogueBox.Text = arrangements.GetValueOrDefault(currentActionId, "");
            ready = true;
        }
        RefreshInputState();
    }
    private static string Choice(ComboBox box) => CurrentChoice(box) ?? throw new InvalidOperationException("モデルの設定を選択してください。");
    private GenerationSettings ReadSettings() => SceneRecipe.Create(SelectedModel.Id, int.Parse(Choice(DurationBox)), ((SceneAction)ActionBox.SelectedItem).Id, DialogueBox.Text);
    private bool CanGenerate()
    {
        if (!ready || busy || submissionUncertain || client is null || !File.Exists(sourcePath) || !File.Exists(referencePath)) return false;
        try { ReadSettings().Body(["https://example.invalid/person.png", "https://example.invalid/background.png"]); return true; }
        catch { return false; }
    }
    private void RefreshInputState()
    {
        GenerateButton.IsEnabled = CanGenerate();
        UpdatePreviewRequest();
    }
    private void UpdatePreviewRequest()
    {
        if (ActionBox.SelectedItem is SceneAction action)
        {
            DialogueHint.Text = action.DialogueHint;
            ArrangementLabel.Text = action.ArrangementLabel;
            DefaultCommentLabel.Text = action.Id == "dance" ? "デフォルトの音楽" : "デフォルトのコメント";
            DefaultComment.Text = action.DefaultComment;
        }
        try
        {
            var s = ReadSettings();
            var urls = s.References ? new[] { "https://example.invalid/character.png", "https://example.invalid/background.png" } : new[] { "https://example.invalid/start.png" };
            TraceBox.Text = "未送信のリクエスト例（APIキー・画像URLは表示しません）\nPOST /" + s.Endpoint + "\n" + ApiClient.SafeJson(s.Body(urls));
        }
        catch { TraceBox.Text = "入力完了後、リクエスト例を表示します。"; }
    }
    private void AppendTrace(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => AppendTrace(message)); return; }
        if (TraceBox.Text.Length > 45000) TraceBox.Text = TraceBox.Text[^20000..];
        TraceBox.AppendText($"\n\n[{DateTime.Now:HH:mm:ss}] {message}"); TraceBox.ScrollToEnd();
    }
    private void SetBusy(bool value, bool polling = false)
    {
        busy = value; SettingsPanel.IsEnabled = !value; AssetList.IsEnabled = !value; BackgroundList.IsEnabled = !value;
        ResumeButton.IsEnabled = !value; HistoryList.IsEnabled = !value;
        GenerateButton.IsEnabled = CanGenerate();
        Progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        PauseButton.Visibility = value && polling ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RequireClient()
    {
        if (client is null) throw new InvalidOperationException("右上の「API設定」でキーを設定してください。");
    }
    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (busy || submissionUncertain) return;
        Job? job = null;
        try
        {
            RequireClient(); var s = ReadSettings();
            if (!File.Exists(sourcePath) || !File.Exists(referencePath)) throw new ArgumentException("人物と背景の画像を選んでください。");
            s.Body(["https://example.invalid/person.png", "https://example.invalid/background.png"]);
            SetBusy(true); operation = new CancellationTokenSource();
            TraceBox.Text = "実際のAPI通信記録（キー・URLは非表示）";
            StatusText.Text = "画像を準備しています…初回のみアップロードし、同じ画像は再利用します。";
            var urls = new List<string> { await client!.UploadAsync(sourcePath, operation.Token) };
            if (s.References) urls.Add(await client.UploadAsync(referencePath, operation.Token));
            var body = s.Body(urls);
            job = new Job { Endpoint = s.Endpoint, Prompt = s.Prompt, InputPath = sourcePath,
                Duration = s.Duration, Resolution = s.Resolution, AspectRatio = s.AspectRatio, Audio = s.Audio, ReferencePath = s.References ? referencePath : "" };
            sessionJobIds.Add(job.Id);
            jobs.Save(job); selectedJob = job;
            StatusText.Text = "生成リクエストを送信しています…";
            // A saved intent remains even if the response is lost. Never retry this POST automatically.
            var response = await client!.SubmitAsync(s.Endpoint, body, operation.Token);
            job.Apply(response); jobs.Save(job); RefreshHistory(job.Id);
            if (!job.Terminal && string.IsNullOrEmpty(job.StatusUrl)) throw new InvalidDataException("状態確認URLがありません。コンソールのRequestsを確認してください。");
            SetBusy(true, true);
            await CompleteJob(job, operation.Token);
        }
        catch (Exception ex)
        {
            if (job is not null && job.Status == "submitting")
            {
                job.Status = ex is ApiException a && a.StatusCode is >= 400 and < 500 ? "rejected" : "submission_unknown";
                submissionUncertain = job.Status == "submission_unknown";
                jobs.Save(job); RefreshHistory(job.Id);
            }
            ShowError(ex);
            if (job?.Status == "submission_unknown") StatusText.Text = "受付結果が不明です。重複課金を避けるため再送せず、公式コンソールのRequestsを確認してください。";
        }
        finally { SetBusy(false); operation?.Dispose(); operation = null; }
    }
    private async Task CompleteJob(Job job, CancellationToken ct)
    {
        StatusText.Text = $"生成状態：{job.Status} · 完了まで待機します";
        await client!.PollAsync(job, j => { jobs.Save(j); StatusText.Text = $"生成状態：{j.Status} · 受付済みの処理を確認中"; RefreshHistory(j.Id); }, ct);
        if (job.Status != "completed") { StatusText.Text = $"生成終了：{job.Status}。内容とコンソールの詳細をご確認ください。"; return; }
        if (string.IsNullOrEmpty(job.VideoUrl)) throw new InvalidDataException("生成は完了しましたが動画URLを取得できません。履歴から状態確認を再開してください。");
        StatusText.Text = "動画をダウンロードしています…";
        var modelId = ModelCatalog.FromEndpoint(job.Endpoint)?.Id ?? "video";
        var output = Path.Combine(App.OutputPath, $"{modelId}-{job.Created:yyyyMMdd-HHmmss}-{job.Id[..6]}.mp4");
        await client.DownloadAsync(job.VideoUrl, output, ct);
        job.LocalVideo = output; jobs.Save(job); RefreshHistory(job.Id);
        videoPath = output; videoModelName = job.ModelName; PlayVideo();
        StatusText.Text = "完成しました。動画はローカルに保存済みです。実際の請求額は公式Billingで確認できます。";
    }
    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (busy || selectedJob is null) return;
        var job = selectedJob;
        try
        {
            if (File.Exists(job.LocalVideo)) { videoPath = job.LocalVideo; videoModelName = job.ModelName; PlayVideo(); return; }
            RequireClient();
            if (string.IsNullOrEmpty(job.StatusUrl)) throw new InvalidOperationException("この履歴には状態確認URLがありません。公式コンソールのRequestsを確認してください。");
            SetBusy(true, true); operation = new CancellationTokenSource();
            job.Apply(await client!.StatusAsync(job.StatusUrl, operation.Token)); jobs.Save(job);
            await CompleteJob(job, operation.Token);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); operation?.Dispose(); operation = null; }
    }
    private void Pause_Click(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void RefreshHistory(string? selectId = null)
    {
        selectId ??= selectedJob?.Id;
        var list = jobs.Load().Where(j => sessionJobIds.Contains(j.Id)).ToList(); HistoryList.ItemsSource = list;
        if (selectId is not null) HistoryList.SelectedItem = list.FirstOrDefault(j => j.Id == selectId);
    }
    private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedJob = HistoryList.SelectedItem as Job;
        if (selectedJob is null || busy) return;
        if (File.Exists(selectedJob.LocalVideo)) { videoPath = selectedJob.LocalVideo; videoModelName = selectedJob.ModelName; PlayVideo(); }
        else { videoPath = ""; PlayButton.IsEnabled = false; ShowSource(); }
    }
    private void ShowError(Exception ex)
    {
        StatusText.Text = ex switch
        {
            OperationCanceledException => "確認を中断しました。受付済みの生成はサーバーで継続します。履歴から確認を再開できます。",
            HttpRequestException => "通信に失敗しました。接続を確認してください。受付済みの生成は履歴から再開できます。",
            _ => ex.Message
        };
        AppendTrace("操作終了：" + StatusText.Text);
    }
    private void ShowSource()
    {
        VideoPreview.Stop(); VideoPreview.Visibility = Visibility.Collapsed; SourcePreview.Visibility = Visibility.Visible;
        if (File.Exists(sourcePath)) { SourcePreview.Source = Bitmap(sourcePath, 1400); EmptyPreview.Visibility = Visibility.Collapsed; }
        PreviewTitle.Text = "選んだキャラと背景"; PreviewDescription.Text = $"キャラ：{Path.GetFileName(sourcePath)}   背景：{Path.GetFileName(referencePath)}";
    }
    private void ShowSource_Click(object sender, RoutedEventArgs e) => ShowSource();
    private void Play_Click(object sender, RoutedEventArgs e) => PlayVideo();
    private void PlayVideo()
    {
        if (!File.Exists(videoPath)) return;
        SourcePreview.Visibility = Visibility.Collapsed; EmptyPreview.Visibility = Visibility.Collapsed; VideoPreview.Visibility = Visibility.Visible;
        VideoPreview.Source = new Uri(videoPath); VideoPreview.Position = TimeSpan.Zero; VideoPreview.Play();
        PreviewTitle.Text = "RESULT / " + videoModelName; PreviewDescription.Text = Path.GetFileName(videoPath);
        PlayButton.IsEnabled = true;
    }
    private void Video_MediaEnded(object sender, RoutedEventArgs e) { VideoPreview.Position = TimeSpan.Zero; VideoPreview.Pause(); }
    private void Video_MediaFailed(object sender, ExceptionRoutedEventArgs e) => StatusText.Text = "内蔵再生に失敗しました。保存先から既定の動画プレイヤーで開いてください。";
    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(App.OutputPath);
            Process.Start(new ProcessStartInfo(App.OutputPath) { UseShellExecute = true });
        }
        catch { StatusText.Text = "outputフォルダを開けませんでした。アプリの配置先の書き込み権限を確認してください。"; }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (busy)
        {
            if (MessageBox.Show(this, "受付済みの生成はサーバーで継続します。次回起動時には履歴を表示しないため、未取得の動画は公式コンソールから確認してください。閉じますか？", "処理中", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { e.Cancel = true; return; }
            operation?.Cancel();
        }
        VideoPreview.Stop();
    }
    public void RunSmokeAssertions()
    {
        if (assets.Count > 0 && SourcePreview.Source is null) throw new Exception("素材プレビューが読み込まれていません。");
        if (GenerateButton.IsEnabled != CanGenerate()) throw new Exception("生成ボタンの有効条件が不一致です。");
        if (!TraceBox.Text.Contains("未送信")) throw new Exception("未送信ログ表示がありません。");
        if (DialogueBox.ActualWidth < 150 || SourcePreview.ActualWidth < 200) throw new Exception("レイアウトの幅が不足しています。");
        if (BackgroundList.Items.Count > 0 && string.IsNullOrEmpty(referencePath)) throw new Exception("背景が読み込まれていません。");
        if (HistoryList.Items.Count != 0) throw new Exception("過去の起動時の履歴が表示されています。");
        void CheckReady() { if (GenerateButton.IsEnabled != CanGenerate()) throw new Exception("入力変更後の生成ボタン状態が不一致です。"); }
        DialogueBox.Text = "こんにちは。"; CheckReady(); DialogueBox.Text = "";
        ActionBox.SelectedIndex = 1; CheckReady(); ActionBox.SelectedIndex = 0;
        foreach (var action in SceneRecipe.Actions)
        {
            ActionBox.SelectedItem = action;
            if (DialogueHint.Text != action.DialogueHint) throw new Exception("行動のセリフ説明が更新されていません。");
            if (DefaultComment.Text != action.DefaultComment || ArrangementLabel.Text != action.ArrangementLabel) throw new Exception("デフォルト表示または入力ラベルが更新されていません。");
        }
        ActionBox.SelectedIndex = 0;
        DialogueBox.Text = "また会おうね！";
        ActionBox.SelectedIndex = 1;
        if (DialogueBox.Text.Length != 0) throw new Exception("セリフが音楽欄に混入しています。");
        DialogueBox.Text = "ゆったりしたジャズ";
        if (!ReadSettings().Prompt.Contains("No speech") || ReadSettings().Prompt.Contains("speaks this exact")) throw new Exception("音楽がセリフとして扱われています。");
        ActionBox.SelectedIndex = 0;
        if (DialogueBox.Text != "また会おうね！") throw new Exception("行動のアレンジが保持されていません。");
        DialogueBox.Text = "";
        ActionBox.SelectedIndex = 1;
        if (DialogueBox.Text != "ゆったりしたジャズ") throw new Exception("音楽アレンジが保持されていません。");
        DialogueBox.Text = "";
        ActionBox.SelectedIndex = 0;
        DurationBox.SelectedIndex = (DurationBox.SelectedIndex + 1) % DurationBox.Items.Count; CheckReady();
        foreach (var model in ModelCatalog.All)
        {
            ModelBox.SelectedItem = model; CheckReady();
            var options = ReadSettings();
            var body = options.Body(["https://example.invalid/person.png", "https://example.invalid/background.png"]);
            if (!options.References || !options.Audio || options.AspectRatio != "16:9") throw new Exception("固定設定不一致。");
            if (options.Resolution != (model.Id == "minimax-h3" ? "2K" : "720p")) throw new Exception("固定解像度不一致。");
            if (options.ModelId != model.Id || !TraceBox.Text.Contains(model.Prefix)) throw new Exception("モデルと送信先の不一致。");
            if (model.SupportsAudioToggle && body["generate_audio"]?.GetValue<bool>() != true) throw new Exception("音声が有効ではありません。");
        }
        ModelBox.SelectedItem = ModelCatalog.Get("wan-3.0");
        FillOptions(DurationBox, Enumerable.Range(2, 29).Select(n => n.ToString()), "6");
        var savedClient = client;
        using (var offlineClient = new ApiClient("smoke-id", "smoke-secret"))
        {
            client = offlineClient; RefreshInputState();
            if (File.Exists(sourcePath) && File.Exists(referencePath) && !GenerateButton.IsEnabled) throw new Exception("見積もりなしで生成準備が整いません。");
            SetBusy(true); if (GenerateButton.IsEnabled) throw new Exception("処理中に再送できます。");
            SetBusy(false); submissionUncertain = true; RefreshInputState();
            if (GenerateButton.IsEnabled) throw new Exception("受付不明のまま再送できます。");
            submissionUncertain = false; client = savedClient; RefreshInputState();
        }
        UpdateLayout();
    }
}
