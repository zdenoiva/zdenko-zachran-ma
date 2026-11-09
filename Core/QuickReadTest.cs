using System.Diagnostics;
using NtfsRescue.Repair;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

public enum QuickReadRating
{
    Unknown,
    Good,
    Caution,
    Bad
}

public sealed class QuickReadResult
{
    public bool Ran;
    public int Samples;
    public long Bytes;
    public double Seconds;
    public double MbPerSecond;
    public double WorstMs;
    public int Errors;
    public bool TimedOut;
    public bool Direct;
    public QuickReadRating Rating = QuickReadRating.Unknown;
    public string Summary = "";
}

/// <summary>Krátky test čítania niekoľkých blokov pri diagnostike (bez dlhého čakania).</summary>
public static class QuickReadTest
{
    public const int BlockSize = 4 * 1024 * 1024;
    private const int SampleCount = 12;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(6);

    public static QuickReadResult Run(int diskNumber, long diskSize, string mediaType,
        string busType, CancellationToken ct)
    {
        var result = new QuickReadResult();
        if (diskNumber < 0 || diskSize < BlockSize) return result;

        int totalBlocks = (int)Math.Min(int.MaxValue, diskSize / BlockSize);
        if (totalBlocks < 1) return result;

        var positions = PickPositions(totalBlocks);
        var task = Task.Run(() => Measure(diskNumber, diskSize, positions, result, ct),
            CancellationToken.None);
        try
        {
            if (!task.Wait(Budget))
            {
                result.TimedOut = true;
                result.Ran = true;
                result.Errors++;
                result.Rating = QuickReadRating.Bad;
                result.Summary = "Rýchly test čítania: časový limit – podozrivo pomalé, odporúčaná plná kontrola povrchu.";
                return result;
            }
        }
        catch (AggregateException)
        {
            result.TimedOut = true;
            result.Errors++;
        }

        result.Ran = true;
        result.MbPerSecond = result.Bytes / 1_000_000.0 /
                             Math.Max(0.001, result.Seconds > 0 ? result.Seconds : Budget.TotalSeconds);
        Classify(result, mediaType, busType);
        result.Summary = BuildSummary(result);
        return result;
    }

    private static int[] PickPositions(int totalBlocks)
    {
        if (totalBlocks <= SampleCount + 2)
            return Enumerable.Range(0, totalBlocks).ToArray();

        // Hustejšie vzorky na začiatku (tam sa najčastejšie prejaví spomalenie)
        // a zvyšok rovnomerne po celom disku.
        double[] fractions =
        {
            0.01, 0.02, 0.03, 0.04, 0.06, 0.08,
            0.15, 0.30, 0.45, 0.60, 0.75, 0.90
        };
        return fractions
            .Select(f => (int)Math.Clamp((long)(totalBlocks * f), 0, totalBlocks - 1))
            .Distinct()
            .ToArray();
    }

    private static void Measure(int diskNumber, long diskSize, int[] positions,
        QuickReadResult result, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        using var reader = new DirectDiskReader(diskNumber);
        result.Direct = reader.Direct;
        var buffer = new byte[BlockSize];

        foreach (var block in positions)
        {
            ct.ThrowIfCancellationRequested();
            long offset = (long)block * BlockSize;
            int length = (int)Math.Min(BlockSize, diskSize - offset);
            if (length < 4096) continue;

            var blockWatch = Stopwatch.StartNew();
            try
            {
                reader.ReadAt(offset, buffer, length);
                result.Samples++;
                result.Bytes += length;
                result.WorstMs = Math.Max(result.WorstMs, blockWatch.Elapsed.TotalMilliseconds);
            }
            catch
            {
                result.Errors++;
                result.WorstMs = Math.Max(result.WorstMs, blockWatch.Elapsed.TotalMilliseconds);
            }

            // Jeden veľmi pomalý blok stačí na ukončenie testu.
            if (blockWatch.Elapsed.TotalSeconds > 4.5)
                break;
        }

        result.Seconds = stopwatch.Elapsed.TotalSeconds;
    }

    private static void Classify(QuickReadResult result, string mediaType, string busType)
    {
        string bus = (busType ?? "").ToUpperInvariant();
        string media = (mediaType ?? "").ToUpperInvariant();
        bool usb = bus.Contains("USB");
        bool nvme = bus.Contains("NVME");
        bool ssd = media.Contains("SSD");

        double bad = usb ? 20 : nvme ? 300 : ssd ? 90 : 30;
        double caution = usb ? 45 : nvme ? 700 : ssd ? 200 : 60;

        if (result.Errors > 0 || result.TimedOut || result.Samples == 0)
        {
            result.Rating = QuickReadRating.Bad;
            return;
        }

        if (result.MbPerSecond < bad) result.Rating = QuickReadRating.Bad;
        else if (result.MbPerSecond < caution) result.Rating = QuickReadRating.Caution;
        else result.Rating = QuickReadRating.Good;
    }

    private static string BuildSummary(QuickReadResult result)
    {
        if (result.TimedOut)
            return "Rýchly test čítania: časový limit – podozrivo pomalé, odporúčaná plná kontrola povrchu.";
        if (result.Errors > 0)
            return "Rýchly test čítania: chyba čítania – odporúčaná plná kontrola povrchu.";
        if (result.Samples == 0)
            return "Rýchly test čítania: nedostupné.";

        var speed = $"{result.MbPerSecond:0.0} MB/s ({result.Samples} blokov)";
        return result.Rating switch
        {
            QuickReadRating.Bad => $"Rýchly test čítania: {speed} – podozrivo pomalé, odporúčaná plná kontrola povrchu.",
            QuickReadRating.Caution => $"Rýchly test čítania: {speed} – pomalšie, odporúčaná kontrola povrchu.",
            _ => $"Rýchly test čítania: {speed} – OK."
        };
    }
}
