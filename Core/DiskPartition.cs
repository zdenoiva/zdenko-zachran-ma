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
    public string DriveLetter = "";
    public string Label = "";
    public string ImagePath = "";
    public bool IsSystem;
    public bool IsGpt;
    public FileSystemKind FileSystem = FileSystemKind.Unknown;

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
