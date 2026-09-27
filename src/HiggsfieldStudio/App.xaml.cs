using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HiggsfieldStudio;

public partial class App : Application
{
    public static string Workspace { get; private set; } = "";
    public static string DataPath => Path.Combine(Workspace, ".local");
    public static string OutputPath => Path.Combine(Workspace, "output");
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "ref"))) dir = dir.Parent;
        Workspace = dir?.FullName ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(DataPath);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Contains("--smoke"))
        {
            window.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await Task.Delay(600);
                    window.UpdateLayout();
                    window.RunSmokeAssertions();
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth - 16, (int)window.ActualHeight - 39, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory(Path.Combine(Workspace, "artifacts"));
                    using (var stream = File.Create(Path.Combine(Workspace, "artifacts", "studio-preview.png"))) encoder.Save(stream);
                    File.WriteAllText(Path.Combine(Workspace, "artifacts", "smoke-result.txt"), "PASS: window loaded; references loaded; no network calls; generate requires credentials and images; busy and unknown submission prevent resubmission; request preview sanitized; three scene models switch; audio and resolution fixed; direct generation without estimate; dialogue, action, duration and model changes refresh request.");
                    Shutdown(0);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(Workspace, "artifacts", "smoke-result.txt"), ex.ToString());
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
        }
    }
}

