using OwlCTF.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace OwlCTF.Tests;

public sealed class SponsorLogoStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "owlctf-sponsor-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SponsorLogoStorage storage;

    public SponsorLogoStorageTests()
    {
        var webRoot = Path.Combine(root, "wwwroot");
        Directory.CreateDirectory(webRoot);
        storage = new SponsorLogoStorage(new TestEnvironment(webRoot));
    }

    [Fact]
    public async Task SavedLogoAppearsInTheRequestedSlot()
    {
        await storage.SaveAsync(2, Upload(PngBytes(), "logo.png"), TestContext.Current.CancellationToken);

        var logo = Assert.Single(storage.List());
        Assert.Equal(2, logo.Slot);
        Assert.StartsWith("/uploads/sponsors/sponsor-2.png?v=", logo.PublicPath);
    }

    [Fact]
    public async Task ReplacingALogoRemovesThePreviousFileType()
    {
        await storage.SaveAsync(1, Upload(PngBytes(), "old.png"), TestContext.Current.CancellationToken);
        await storage.SaveAsync(1, Upload(JpegBytes(), "new.jpg"), TestContext.Current.CancellationToken);

        var logo = Assert.Single(storage.List());
        Assert.Contains("sponsor-1.jpg", logo.PublicPath);
        Assert.False(File.Exists(Path.Combine(root, "wwwroot", "uploads", "sponsors", "sponsor-1.png")));
    }

    [Fact]
    public async Task UnsupportedContentIsRejectedRegardlessOfFileName()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveAsync(1, Upload("not an image"u8.ToArray(), "logo.png"), TestContext.Current.CancellationToken));

        Assert.Contains("PNG, JPEG or WebP", error.Message);
        Assert.Empty(storage.List());
    }

    [Fact]
    public async Task DeleteRemovesOnlyTheSelectedSlot()
    {
        await storage.SaveAsync(1, Upload(PngBytes(), "one.png"), TestContext.Current.CancellationToken);
        await storage.SaveAsync(2, Upload(PngBytes(), "two.png"), TestContext.Current.CancellationToken);

        Assert.True(storage.Delete(1));
        var remaining = Assert.Single(storage.List());
        Assert.Equal(2, remaining.Slot);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private static FormFile Upload(byte[] bytes, string name) => new(new MemoryStream(bytes), 0, bytes.Length, "logo", name);
    private static byte[] PngBytes() => [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];
    private static byte[] JpegBytes() => [255, 216, 255, 224, 0, 16, 74, 70, 73, 70, 0, 0];

    private sealed class TestEnvironment(string webRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "OwlCTF.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = webRoot;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = webRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
