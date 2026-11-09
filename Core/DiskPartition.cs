using NtfsRescue.Util;

namespace NtfsRescue.Core;

public sealed class DiskPartition
{
    public int DiskNumber;
    public int PartitionNumber;
    public long Offset;
    public long Length;
    public int SectorSize = 512;
    public long DiskSize;
    public string Model = "";
    public string Serial = "";
    public string PartitionType = "";
    public string GptType = "";
    public string MbrType = "";
    public string DriveLetter = "";
    public string Label = "";
    public string ImagePath = "";
    public bool IsSystem;
    public bool IsGpt;
    public bool IsActive;
    public bool IsHidden;
    public bool IsOffline;
    public bool IsReadOnly;
    public string PartitionStyle = "";
    public string DiskGuid = "";
    public string DiskSignature = "";
    public bool DiskIsBoot;
    public bool DiskIsSystem;
    public bool DiskIsOffline;
    public bool DiskIsReadOnly;
    public string BusType = "";
    public FileSystemKind FileSystem = FileSystemKind.Unknown;

    public const string EspGptType = "c12a7328-f81f-11d2-ba4b-00a0c93ec93b";
    public const string MsrGptType = "e3c9e316-0b5c-4db8-817d-f92df00215ae";
    public const string WindowsGptType = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7";
    public const string RecoveryGptType = "de94bba4-06d1-4d40-a16a-bfd50179d6ac";

    public bool IsEsp =>
        (IsGpt && GptType.Equals(EspGptType, StringComparison.OrdinalIgnoreCase)) ||
        (!IsGpt && (MbrType == "239" || MbrType.Equals("0xEF", StringComparison.OrdinalIgnoreCase)));

    public bool IsRecovery =>
        (IsGpt && GptType.Equals(RecoveryGptType, StringComparison.OrdinalIgnoreCase)) ||
        (!IsGpt && MbrType == "39") ||
        PartitionType.Contains("Recovery", StringComparison.OrdinalIgnoreCase);

    public bool IsImage => !string.IsNullOrWhiteSpace(ImagePath);
    public bool HasLetter => !IsImage && DriveLetter.Length == 1 && DriveLetter[0] is >= 'A' and <= 'Z';
    public char Letter => HasLetter ? DriveLetter[0] : '\0';
    public string RootPath => HasLetter ? $"{DriveLetter}:\\" : "";

    public RawDisk OpenDisk()
        => IsImage
            ? RawDisk.OpenFile(ImagePath, SectorSize)
            : new RawDisk(DiskNumber, SectorSize, DiskSize);

    public string Display
    {
        get
        {
            if (IsImage)
            {
                var imageParts = new List<string>
                {
                    "Obraz: " + Path.GetFileName(ImagePath),
                    Fmt.Bytes(Length),
                    FileSystem.ToSlovak()
                };
                return string.Join("  ·  ", imageParts);
            }

            var parts = new List<string>
            {
                $"Disk {DiskNumber} · partícia {PartitionNumber}",
                Fmt.Bytes(Length),
                FileSystem.ToSlovak()
            };
            if (HasLetter) parts.Add($"{DriveLetter}:");
            else parts.Add("bez písmena");
            if (IsSystem) parts.Add("systémová");
            return string.Join("  ·  ", parts);
        }
    }

    public override string ToString() => Display;
}
