namespace NtfsRescue.Util;

public static class Fmt
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Bytes(long value)
    {
        if (value < 0) return "-";
        double v = value;
        int u = 0;
        while (v >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{v:0} {Units[u]}" : $"{v:0.##} {Units[u]}";
    }

    public static string Date(DateTime value)
    {
        if (value <= DateTime.MinValue || value >= DateTime.MaxValue) return "";
        return value.ToString("yyyy-MM-dd HH:mm");
    }

    public static string Hex(long value) => "0x" + value.ToString("X");
}
