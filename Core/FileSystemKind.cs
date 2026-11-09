namespace NtfsRescue.Core;

public enum FileSystemKind
{
    Unknown,
    Raw,
    Ntfs,
    Fat32,
    ExFat
}

public static class FileSystemKindText
{
    public static string ToSlovak(this FileSystemKind kind) => kind switch
    {
        FileSystemKind.Ntfs => "NTFS",
        FileSystemKind.Fat32 => "FAT32",
        FileSystemKind.ExFat => "exFAT",
        FileSystemKind.Raw => "RAW",
        _ => "neznámy"
    };
}
