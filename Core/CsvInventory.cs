using System.Text;

namespace NtfsRescue.Core;

public static class CsvInventory
{
    public static async Task WriteAsync(IEnumerable<NtfsEntry> entries, string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        await using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        await writer.WriteLineAsync("Record;Parent;Nazov;Cesta;Velkost;Upravene;Atributy;Zmazany;Stav");

        int count = 0;
        long total = 0;

        foreach (var e in entries)
        {
            if ((count & 0x3FF) == 0) ct.ThrowIfCancellationRequested();

            var line = string.Join(';',
                e.RecordNumber.ToString(),
                e.ParentRecord.ToString(),
                Csv(e.Name),
                Csv(e.FullPath),
                e.Size.ToString(),
                Csv(e.Modified == DateTime.MinValue ? "" : e.Modified.ToString("yyyy-MM-dd HH:mm:ss")),
                "0x" + e.FileAttributes.ToString("X8"),
                e.Deleted ? "1" : "0",
                Csv(e.Status));

            await writer.WriteLineAsync(line).ConfigureAwait(false);
            count++;
            total += e.Size;
        }

        await writer.WriteLineAsync().ConfigureAwait(false);
        await writer.WriteLineAsync($";;;;POCET;{count};;;;").ConfigureAwait(false);
        await writer.WriteLineAsync($";;;;SUCET;{total};;;;").ConfigureAwait(false);
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task WriteAsync(IEnumerable<RecoveryEntry> entries, string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        await using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        await writer.WriteLineAsync("Nazov;Cesta;Velkost;Upravene;Stav;Zmazany").ConfigureAwait(false);

        int count = 0;
        long total = 0;

        foreach (var e in entries)
        {
            if ((count & 0x3FF) == 0) ct.ThrowIfCancellationRequested();
            var line = string.Join(';',
                Csv(e.Name),
                Csv(e.FullPath),
                e.Size.ToString(),
                Csv(e.Modified == DateTime.MinValue ? "" : e.Modified.ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(e.Status),
                e.Deleted ? "1" : "0");

            await writer.WriteLineAsync(line).ConfigureAwait(false);
            count++;
            total += e.Size;
        }

        await writer.WriteLineAsync().ConfigureAwait(false);
        await writer.WriteLineAsync($";;POCET;{count};;").ConfigureAwait(false);
        await writer.WriteLineAsync($";;SUCET;{total};;").ConfigureAwait(false);
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    private static string Csv(string value)
    {
        if (value.IndexOfAny(new[] { ';', '"', '\r', '\n' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
