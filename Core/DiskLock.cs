using System.Text;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

/// <summary>
/// Medziprocesové zamknutie fyzického disku počas dlhých operácií.
/// Druhá inštancia programu (alebo iná operácia) nedostane prístup.
/// </summary>
public sealed class DiskLock : IDisposable
{
    private readonly FileStream? _stream;
    private readonly string? _path;

    public bool Acquired { get; }
    public string Owner { get; }

    private DiskLock(FileStream? stream, string? path, bool acquired, string owner)
    {
        _stream = stream;
        _path = path;
        Acquired = acquired;
        Owner = owner;
    }

    public static DiskLock TryAcquire(int diskNumber, string purpose, out string owner)
    {
        owner = "";
        string path;
        try
        {
            var directory = Path.Combine(AppPaths.Base, "locks");
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, $"disk-{diskNumber}.lock");
        }
        catch
        {
            // Ak sa nedá vytvoriť priečinok, nebránime operácii.
            return new DiskLock(null, null, true, "");
        }

        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None);
            var text = $"{Environment.ProcessId}|{purpose}|{DateTime.Now:O}";
            var bytes = Encoding.UTF8.GetBytes(text);
            stream.SetLength(0);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            return new DiskLock(stream, path, true, "");
        }
        catch (IOException)
        {
            owner = ReadOwner(path);
            return new DiskLock(null, null, false, owner);
        }
        catch
        {
            return new DiskLock(null, null, true, "");
        }
    }

    private static string ReadOwner(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            var parts = text.Split('|');
            if (parts.Length >= 2 && int.TryParse(parts[0], out int pid))
                return $"PID {pid}: {parts[1]}";
        }
        catch { }
        return "";
    }

    public void Dispose()
    {
        try { _stream?.Dispose(); } catch { }
        try { if (_path is not null) File.Delete(_path); } catch { }
    }
}
