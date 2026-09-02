using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using OwlCTF.Options;
using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class BrandingStorageTests : IDisposable
{
    private readonly StorageFixture fixture = new("branding");
    private readonly BrandingStorage storage;

    public BrandingStorageTests() => storage = new BrandingStorage(fixture.Environment);

    [Fact]
    public async Task NavbarLogoUsesAContentVerifiedExtension()
    {
        var path = await storage.SaveLogoAsync(TestUploads.File(TestUploads.Png, "misleading.jpg"), TestContext.Current.CancellationToken);

        Assert.StartsWith("/uploads/branding/navbar-", path);
        Assert.EndsWith(".png", path);
        Assert.True(File.Exists(fixture.PhysicalPath(path)));
    }

    [Fact]
    public async Task FaviconAcceptsIconFiles()
    {
        var path = await storage.SaveFaviconAsync(TestUploads.File([0, 0, 1, 0, 1, 0, 16, 16, 0, 0, 1, 0], "owl.ico"), TestContext.Current.CancellationToken);

        Assert.StartsWith("/uploads/branding/favicon-", path);
        Assert.EndsWith(".ico", path);
    }

    [Fact]
    public async Task FaviconRejectsUnknownContent()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveFaviconAsync(TestUploads.File("not an icon"u8.ToArray(), "owl.png"), TestContext.Current.CancellationToken));

        Assert.Contains("PNG, ICO, JPEG or WebP", error.Message);
    }

    [Fact]
    public async Task DeletingACustomAssetDoesNotDeleteBuiltInBranding()
    {
        var customPath = await storage.SaveFaviconAsync(TestUploads.File(TestUploads.Png, "owl.png"), TestContext.Current.CancellationToken);

        storage.DeleteCustomAsset(customPath);
        storage.DeleteCustomAsset(BrandingStorage.DefaultFaviconPath);

        Assert.False(File.Exists(fixture.PhysicalPath(customPath)));
    }

    public void Dispose() => fixture.Dispose();
}

public sealed class SponsorLogoStorageTests : IDisposable
{
    private readonly StorageFixture fixture = new("sponsor");
    private readonly SponsorLogoStorage storage;

    public SponsorLogoStorageTests() => storage = new SponsorLogoStorage(fixture.Environment);

    [Fact]
    public async Task SavedLogoAppearsInTheRequestedSlot()
    {
        await storage.SaveAsync(2, TestUploads.File(TestUploads.Png, "logo.png"), TestContext.Current.CancellationToken);

        var logo = Assert.Single(storage.List());
        Assert.Equal(2, logo.Slot);
        Assert.StartsWith("/uploads/sponsors/sponsor-2.png?v=", logo.PublicPath);
    }

    [Fact]
    public async Task ReplacingALogoRemovesThePreviousFileType()
    {
        await storage.SaveAsync(1, TestUploads.File(TestUploads.Png, "old.png"), TestContext.Current.CancellationToken);
        await storage.SaveAsync(1, TestUploads.File(TestUploads.Jpeg, "new.jpg"), TestContext.Current.CancellationToken);

        var logo = Assert.Single(storage.List());
        Assert.Contains("sponsor-1.jpg", logo.PublicPath);
        Assert.False(File.Exists(Path.Combine(fixture.WebRoot, "uploads", "sponsors", "sponsor-1.png")));
    }

    [Fact]
    public async Task UnsupportedContentIsRejectedRegardlessOfFileName()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveAsync(1, TestUploads.File("not an image"u8.ToArray(), "logo.png"), TestContext.Current.CancellationToken));

        Assert.Empty(storage.List());
    }

    [Fact]
    public async Task DeleteRemovesOnlyTheSelectedSlot()
    {
        await storage.SaveAsync(1, TestUploads.File(TestUploads.Png, "one.png"), TestContext.Current.CancellationToken);
        await storage.SaveAsync(2, TestUploads.File(TestUploads.Png, "two.png"), TestContext.Current.CancellationToken);

        Assert.True(storage.Delete(1));
        Assert.Equal(2, Assert.Single(storage.List()).Slot);
    }

    public void Dispose() => fixture.Dispose();
}

public sealed class ContentImageStorageTests : IDisposable
{
    private readonly StorageFixture fixture = new("content-images");
    private readonly ContentImageStorage storage;

    public ContentImageStorageTests() => storage = new ContentImageStorage(fixture.Environment);

    [Fact]
    public async Task ImageTypeComesFromContentInsteadOfTheUploadedName()
    {
        var cases = new[]
        {
            (TestUploads.Png, ".png"),
            (TestUploads.Jpeg, ".jpg"),
            ("GIF89a-extra"u8.ToArray(), ".gif"),
            ("RIFF0000WEBP"u8.ToArray(), ".webp")
        };

        foreach (var (bytes, extension) in cases)
        {
            var saved = await storage.SaveAsync(TestUploads.File(bytes, "spoofed.txt"), TestContext.Current.CancellationToken);
            Assert.EndsWith(extension, saved.FileName);
            Assert.True(File.Exists(fixture.PhysicalPath(saved.PublicPath)));
        }

        Assert.Equal(4, storage.List().Count);
    }

    [Fact]
    public async Task EmptyAndSpoofedImagesAreRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync(TestUploads.File([], "empty.png"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync(TestUploads.File("plain text"u8.ToArray(), "fake.png"), TestContext.Current.CancellationToken));
        Assert.Empty(storage.List());
    }

    [Fact]
    public async Task DeleteRejectsTraversalAndRemovesOnlySavedContentImages()
    {
        var saved = await storage.SaveAsync(TestUploads.File(TestUploads.Png, "image.png"), TestContext.Current.CancellationToken);

        Assert.False(storage.Delete("../" + saved.FileName));
        Assert.False(storage.Delete("unrelated.png"));
        Assert.True(storage.Delete(saved.FileName));
        Assert.False(storage.Delete(saved.FileName));
    }

    public void Dispose() => fixture.Dispose();
}

public sealed class ChallengeFileStorageTests : IDisposable
{
    private readonly StorageFixture fixture = new("challenge-files");
    private readonly FileStorage storage;

    public ChallengeFileStorageTests() =>
        storage = new FileStorage(fixture.Environment, Microsoft.Extensions.Options.Options.Create(new StorageOptions { RootPath = "private-files", MaxFileBytes = 16 }));

    [Fact]
    public async Task SavedFileCanBeReadAndHasAnAccurateDigest()
    {
        var bytes = "flag material"u8.ToArray();
        var saved = await storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

        Assert.Equal(bytes.Length, saved.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), saved.Sha256);
        await using var stream = storage.OpenRead(saved.StorageName);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, copy.ToArray());
        await stream.DisposeAsync();

        storage.Delete(saved.StorageName);
        Assert.Throws<FileNotFoundException>(() => storage.OpenRead(saved.StorageName));
    }

    [Fact]
    public async Task OversizedFileIsRejectedAndPartialFileIsRemoved()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveAsync(new MemoryStream(new byte[17]), TestContext.Current.CancellationToken));

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(fixture.Root, "private-files")));
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("folder/file.bin")]
    public void StorageKeysCannotEscapeTheirDirectory(string key)
    {
        Assert.Throws<InvalidOperationException>(() => storage.OpenRead(key));
        Assert.Throws<InvalidOperationException>(() => storage.Delete(key));
    }

    public void Dispose() => fixture.Dispose();
}

internal sealed class StorageFixture : IDisposable
{
    public StorageFixture(string label)
    {
        Root = Path.Combine(Path.GetTempPath(), $"owlctf-{label}-tests-{Guid.NewGuid():N}");
        WebRoot = Path.Combine(Root, "wwwroot");
        Directory.CreateDirectory(WebRoot);
        Environment = new TestWebHostEnvironment(Root, WebRoot);
    }

    public string Root { get; }
    public string WebRoot { get; }
    public IWebHostEnvironment Environment { get; }

    public string PhysicalPath(string publicPath) =>
        Path.Combine(WebRoot, publicPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}

internal static class TestUploads
{
    public static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];
    public static readonly byte[] Jpeg = [255, 216, 255, 224, 0, 16, 74, 70, 73, 70, 0, 0];
    public static FormFile File(byte[] bytes, string name) => new(new MemoryStream(bytes), 0, bytes.Length, "upload", name);
}

internal sealed class TestWebHostEnvironment(string contentRoot, string webRoot) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "OwlCTF.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = webRoot;
    public string EnvironmentName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = contentRoot;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
