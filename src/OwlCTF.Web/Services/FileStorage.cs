using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OwlCTF.Options;

namespace OwlCTF.Services;

public sealed record StoredFile(string StorageName, long SizeBytes, string Sha256);
public sealed class FileStorage
{
    private readonly string _root;
    public long MaxFileBytes { get; }
    public FileStorage(IWebHostEnvironment environment, IOptions<StorageOptions> options)
    {
        MaxFileBytes = options.Value.MaxFileBytes;
        _root = Path.GetFullPath(Path.IsPathRooted(options.Value.RootPath) ? options.Value.RootPath : Path.Combine(environment.ContentRootPath, options.Value.RootPath));
        Directory.CreateDirectory(_root);
    }
    public async Task<StoredFile> SaveAsync(Stream input, CancellationToken cancellationToken)
    {
        var name = $"{Guid.NewGuid():N}.bin";
        var path = SafePath(name);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > MaxFileBytes) throw new InvalidOperationException("File exceeds the configured size limit.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            return new(name, total, Convert.ToHexString(hash.GetHashAndReset()));
        }
        catch { output.Close(); File.Delete(path); throw; }
    }
    public FileStream OpenRead(string storageName) => new(SafePath(storageName), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
    public void Delete(string storageName) { var path = SafePath(storageName); if (File.Exists(path)) File.Delete(path); }
    private string SafePath(string name)
    {
        if (name != Path.GetFileName(name)) throw new InvalidOperationException("Invalid storage key.");
        var path = Path.GetFullPath(Path.Combine(_root, name));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid storage path.");
        return path;
    }
}
