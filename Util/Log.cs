using System.Text;

namespace NtfsRescue.Util;

public static class AppPaths
{
    public static string Base =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZdenkoZachranMa");

    public static string Logs => Path.Combine(Base, "logs");

    public static string Backups =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ZdenkoZachranMa", "zalohy");

    public static string Inventories =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ZdenkoZachranMa", "inventar");

    public static string RecoveryDefault =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ZdenkoZachranMa", "zachrana");

    public static string BootBackups =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ZdenkoZachranMa", "boot-backup");
}

public static class Log
{
    private static readonly object Gate = new();
    private static readonly List<string> Buffer = new();
    public static string? FilePath { get; set; }

    public static event Action<string>? Appended;

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Action<string>? handler;
        lock (Gate)
        {
            Buffer.Add(line);
            if (Buffer.Count > 2000) Buffer.RemoveRange(0, Buffer.Count - 2000);

            if (FilePath is not null)
            {
                try
                {
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // logovanie nesmie zhodiť aplikáciu
                }
            }

            handler = Appended;
        }

        try { handler?.Invoke(line); } catch { }
    }

    public static void Info(string message) => Write(message);
    public static void Warn(string message) => Write("VAROVANIE: " + message);
    public static void Error(string message) => Write("CHYBA: " + message);
    public static void Ok(string message) => Write("OK: " + message);

    public static string[] Snapshot()
    {
        lock (Gate) return Buffer.ToArray();
    }

    public static void SaveSnapshot(string path)
    {
        var text = string.Join(Environment.NewLine, Snapshot());
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}
