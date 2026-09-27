namespace HiggsfieldStudio.Core;

public static class ApiCredentials
{
    public static (string Id, string Secret) Parse(string? input)
    {
        var parts = (input ?? "").Trim().Split(':');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0 || p.Any(char.IsWhiteSpace)))
            throw new ArgumentException("生成時にコピーした「ID:APIキー」の全文を貼り付けてください。IDだけでは設定できません。");
        return (parts[0], parts[1]);
    }
}
