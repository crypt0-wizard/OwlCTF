namespace OwlCTF.Services;

public sealed class BrandingStorage
{
    public const long MaxLogoBytes = 2 * 1024 * 1024;
    public const long MaxFaviconBytes = 1024 * 1024;
    public const string AdaptiveLogoPath = "/images/owlctf-navbar-mark.svg";
    public const string DefaultLogoPath = "/images/navbar-logo.png";
    public const string DefaultFaviconPath = "/images/favicon.png";
    private const string PublicPrefix = "/uploads/branding/";
    private readonly string _root;

    public BrandingStorage(IWebHostEnvironment environment)
    {
        _root = Path.GetFullPath(Path.Combine(environment.WebRootPath, "uploads", "branding"));
        Directory.CreateDirectory(_root);
    }

    public Task<string> SaveLogoAsync(IFormFile upload, CancellationToken ct) =>
        SaveAsync(upload, "navbar", MaxLogoBytes, false, "Choose a PNG, GIF, JPEG or WebP logo.", ct);

    public Task<string> SaveFaviconAsync(IFormFile upload, CancellationToken ct) =>
        SaveAsync(upload, "favicon", MaxFaviconBytes, true, "Choose a PNG, ICO, JPEG or WebP favicon.", ct);

    private async Task<string> SaveAsync(
        IFormFile upload,
        string filePrefix,
        long maxBytes,
        bool allowIcon,
        string invalidTypeMessage,
        CancellationToken ct)
    {
        if (upload.Length <= 0 || upload.Length > maxBytes)
            throw new InvalidOperationException($"Choose an image up to {maxBytes / (1024 * 1024)} MB.");

        await using var input = upload.OpenReadStream();
        var header = new byte[12];
        var headerLength = 0;
        while (headerLength < header.Length)
        {
            var read = await input.ReadAsync(header.AsMemory(headerLength, header.Length - headerLength), ct);
            if (read == 0) break;
            headerLength += read;
        }

        var extension = DetectExtension(header.AsSpan(0, headerLength), allowIcon)
            ?? throw new InvalidOperationException(invalidTypeMessage);
        var name = $"{filePrefix}-{Guid.NewGuid():N}{extension}";
        var path = SafePath(name);

        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await output.WriteAsync(header.AsMemory(0, headerLength), ct);
        var buffer = new byte[81920];
        long total = headerLength;
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > maxBytes) throw new InvalidOperationException($"Choose an image up to {maxBytes / (1024 * 1024)} MB.");
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            if (extension == ".gif")
            {
                await output.DisposeAsync();
                ValidateGif(await File.ReadAllBytesAsync(path, ct));
            }
        }
        catch
        {
            output.Close();
            File.Delete(path);
            throw;
        }

        return PublicPrefix + name;
    }

    public void DeleteCustomAsset(string? publicPath)
    {
        if (string.IsNullOrWhiteSpace(publicPath) || !publicPath.StartsWith(PublicPrefix, StringComparison.Ordinal)) return;
        var name = publicPath[PublicPrefix.Length..];
        var path = SafePath(name);
        if (File.Exists(path)) File.Delete(path);
    }

    private string SafePath(string name)
    {
        if (name != Path.GetFileName(name)) throw new InvalidOperationException("Invalid logo path.");
        var path = Path.GetFullPath(Path.Combine(_root, name));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid logo path.");
        return path;
    }

    private static string? DetectExtension(ReadOnlySpan<byte> header, bool allowIcon)
    {
        if (!allowIcon && header.Length >= 6
            && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8))) return ".gif";
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (allowIcon && header.Length >= 4 && header[..4].SequenceEqual(new byte[] { 0, 0, 1, 0 })) return ".ico";
        if (header.Length >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255) return ".jpg";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }

    private static void ValidateGif(byte[] bytes)
    {
        var offset = 6;
        int ReadByte()
        {
            if (offset >= bytes.Length) throw new InvalidOperationException("The GIF is incomplete or invalid.");
            return bytes[offset++];
        }
        int ReadWord() => ReadByte() | (ReadByte() << 8);
        void Skip(int count)
        {
            if (count > bytes.Length - offset) throw new InvalidOperationException("The GIF is incomplete or invalid.");
            offset += count;
        }
        void Blocks()
        {
            int length;
            while ((length = ReadByte()) != 0) Skip(length);
        }
        var width = ReadWord();
        var height = ReadWord();
        if (width is < 1 or > 4096 || height is < 1 or > 4096)
            throw new InvalidOperationException("GIF dimensions must be between 1 and 4096 pixels.");
        var flags = ReadByte();
        Skip(2);
        var globalPalette = (flags & 128) != 0;
        if (globalPalette) Skip(3 * (1 << ((flags & 7) + 1)));
        var frames = 0;
        while (true)
        {
            switch (ReadByte())
            {
                case 0x21:
                    ReadByte();
                    Blocks();
                    break;
                case 0x2c:
                    var left = ReadWord();
                    var top = ReadWord();
                    var frameWidth = ReadWord();
                    var frameHeight = ReadWord();
                    if (frameWidth == 0 || frameHeight == 0 || left + frameWidth > width || top + frameHeight > height)
                        throw new InvalidOperationException("The GIF contains an invalid frame.");
                    if (++frames > 300 || (long)width * height * frames > 100_000_000)
                        throw new InvalidOperationException("The GIF animation is too large. Use fewer or smaller frames.");
                    var frameFlags = ReadByte();
                    var localPalette = (frameFlags & 128) != 0;
                    if (!localPalette && !globalPalette) throw new InvalidOperationException("The GIF has no colour table.");
                    if (localPalette) Skip(3 * (1 << ((frameFlags & 7) + 1)));
                    var codeSize = ReadByte();
                    if (codeSize is < 2 or > 8) throw new InvalidOperationException("The GIF contains invalid image data.");
                    var firstBlock = ReadByte();
                    if (firstBlock == 0) throw new InvalidOperationException("The GIF contains an empty frame.");
                    Skip(firstBlock);
                    Blocks();
                    break;
                case 0x3b:
                    if (frames > 0 && offset == bytes.Length) return;
                    throw new InvalidOperationException("The GIF contains no frames or unexpected trailing data.");
                default:
                    throw new InvalidOperationException("The GIF is incomplete or invalid.");
            }
        }
    }
}
