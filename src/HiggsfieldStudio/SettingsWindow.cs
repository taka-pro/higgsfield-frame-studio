using System.Windows;
using System.Windows.Controls;
using HiggsfieldStudio.Core;

namespace HiggsfieldStudio;

internal sealed class SettingsWindow : Window
{
    public string KeyId { get; private set; } = "";
    public string Secret { get; private set; } = "";
    public bool Remember { get; private set; }
    public SettingsWindow(string id, string secret)
    {
        Style = (Style)FindResource(typeof(Window));
        Title = "API設定 — 録画前に設定してください";
        Width = 480; Height = 420; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(26) };
        Content = stack;
        stack.Children.Add(new TextBlock { Text = "Higgsfield API", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
        stack.Children.Add(new TextBlock { Text = "キーの生成時に「ID:APIキー」を全部コピーして、下の欄にそのまま貼り付けてください。分けて入力する必要はありません。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        stack.Children.Add(new TextBlock { Text = "APIキー（ID:APIキーの全文）", Margin = new Thickness(0, 0, 0, 6) });
        var credentialsBox = new PasswordBox { Password = string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret) ? "" : id + ":" + secret }; stack.Children.Add(credentialsBox);
        stack.Children.Add(new TextBlock { Text = "入力内容は伏せ字で表示します。\nAPIキー本体は生成時しかコピーできません。あとからコピーできるIDだけでは利用できません。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        var remember = new CheckBox { Content = "このWindowsユーザー用に暗号化して保存", IsChecked = false, Margin = new Thickness(0, 18, 0, 14) }; stack.Children.Add(remember);
        var apply = new Button { Content = "設定を適用", Padding = new Thickness(14, 10, 14, 10) }; stack.Children.Add(apply);
        apply.Click += (_, _) =>
        {
            try { (KeyId, Secret) = ApiCredentials.Parse(credentialsBox.Password); }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "API設定"); return; }
            Remember = remember.IsChecked == true;
            DialogResult = true;
        };
    }
}
