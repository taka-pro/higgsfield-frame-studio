using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HiggsfieldStudio;

// Windows DPAPI, scoped to the current Windows user. No secrets in source, settings JSON or logs.
internal static class CredentialVault
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static string FilePath => Path.Combine(App.DataPath, "api-key.bin");
    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = encrypt ? CryptProtectData(ref input, "Frame Studio API", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new InvalidOperationException("Windowsによるキーの暗号化／復号に失敗しました。");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, output.Length);
            return result;
        }
        finally
        {
            for (int i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            Array.Clear(bytes);
        }
    }
    public static void Save(string id, string secret)
    {
        File.WriteAllBytes(FilePath, Transform(Encoding.UTF8.GetBytes(id + ":" + secret), true));
    }
    public static (string Id, string Secret) Load()
    {
        if (!File.Exists(FilePath)) return ("", "");
        var bytes = Transform(File.ReadAllBytes(FilePath), false);
        try
        {
            var parts = Encoding.UTF8.GetString(bytes).Split(':', 2);
            return parts.Length == 2 ? (parts[0], parts[1]) : ("", "");
        }
        finally { Array.Clear(bytes); }
    }
    public static void Delete() { if (File.Exists(FilePath)) File.Delete(FilePath); }
}
