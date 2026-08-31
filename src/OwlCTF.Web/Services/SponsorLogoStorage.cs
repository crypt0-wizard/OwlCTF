namespace OwlCTF.Services;

public sealed record SponsorLogoRecord(int Slot, string PublicPath);

public sealed class SponsorLogoStorage
{
    public const int SlotCount = 3;
    public const long MaxImageBytes = 3 * 1024 * 1024;
    private const string PublicPrefix = "/uploads/sponsors/";
    private readonly string root;

    public SponsorLogoStorage(IWebHostEnvironment environment)
    {
        root = Path.GetFullPath(Path.Combine(environment.WebRootPath, "uploads", "sponsors"));
        Directory.CreateDirectory(root);
    }

    public IReadOnlyList<SponsorLogoRecord> List() => Enumerable.Range(1, SlotCount)
        .Select(Find)
        .Where(logo => logo is not null)
        .Cast<SponsorLogoRecord>()
        .ToArray();

    public async Task<SponsorLogoRecord> SaveAsync(int slot, IFormFile upload, CancellationToken ct)
    {
        ValidateSlot(slot);
        if (upload.Length <= 0 || upload.Length > MaxImageBytes)
            throw new InvalidOperationException("Choose a sponsor image up to 3 MB.");

        var temporaryPath = Path.Combine(root, $"upload-{Guid.NewGuid():N}.tmp");
        string? extension = null;
        try
        {
            await using var input = upload.OpenReadStream();
            var header = new byte[12];
            var headerLength = 0;
            while (headerLength < header.Length)
            {
                var read = await input.ReadAsync(header.AsMemory(headerLength, header.Length - headerLength), ct);
                if (read == 0) break;
                headerLength += read;
            }
            extension = DetectExtension(header.AsSpan(0, headerLength))
                ?? throw new InvalidOperationException("Choose a PNG, JPEG or WebP sponsor image.");

            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(header.AsMemory(0, headerLength), ct);
                var buffer = new byte[81920];
                long total = headerLength;
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > MaxImageBytes) throw new InvalidOperationException("Choose a sponsor image up to 3 MB.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            var finalName = $"sponsor-{slot}{extension}";
            var finalPath = Path.Combine(root, finalName);
            File.Move(temporaryPath, finalPath, true);
            foreach (var oldPath in Directory.EnumerateFiles(root, $"sponsor-{slot}.*", SearchOption.TopDirectoryOnly))
                if (!oldPath.Equals(finalPath, StringComparison.OrdinalIgnoreCase)) File.Delete(oldPath);
            return CreateRecord(slot, finalName);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public bool Delete(int slot)
    {
        ValidateSlot(slot);
        var removed = false;
        foreach (var path in Directory.EnumerateFiles(root, $"sponsor-{slot}.*", SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
            removed = true;
        }
        return removed;
    }

    private SponsorLogoRecord? Find(int slot)
    {
        var path = Directory.EnumerateFiles(root, $"sponsor-{slot}.*", SearchOption.TopDirectoryOnly).FirstOrDefault();
        return path is null ? null : CreateRecord(slot, Path.GetFileName(path));
    }

    private SponsorLogoRecord CreateRecord(int slot, string fileName)
    {
        var version = File.GetLastWriteTimeUtc(Path.Combine(root, fileName)).Ticks;
        return new(slot, $"{PublicPrefix}{fileName}?v={version}");
    }

    private static void ValidateSlot(int slot)
    {
        if (slot is < 1 or > SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
    }

    private static string? DetectExtension(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (header.Length >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255) return ".jpg";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }
}
