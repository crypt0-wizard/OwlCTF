using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class BrandingStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "owlctf-branding-tests-" + Guid.NewGuid().ToString("N"));
    private readonly BrandingStorage storage;

    public BrandingStorageTests()
    {
        var webRoot = Path.Combine(root, "wwwroot");
        Directory.CreateDirectory(webRoot);
        storage = new BrandingStorage(new TestEnvironment(webRoot));
    }

    [Fact]
    public async Task NavbarLogoUsesAContentVerifiedExtension()
    {
        var path = await storage.SaveLogoAsync(
            Upload(PngBytes(), "misleading.jpg"),
            TestContext.Current.CancellationToken);

        Assert.StartsWith("/uploads/branding/navbar-", path);
        Assert.EndsWith(".png", path);
        Assert.True(File.Exists(ToPhysicalPath(path)));
    }

    [Fact]
    public async Task FaviconAcceptsIconFiles()
    {
        var path = await storage.SaveFaviconAsync(
            Upload([0, 0, 1, 0, 1, 0, 16, 16, 0, 0, 1, 0], "owl.ico"),
            TestContext.Current.CancellationToken);

        Assert.StartsWith("/uploads/branding/favicon-", path);
        Assert.EndsWith(".ico", path);
    }

    [Fact]
    public async Task FaviconRejectsUnknownContent()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveFaviconAsync(
                Upload("not an icon"u8.ToArray(), "owl.png"),
                TestContext.Current.CancellationToken));

        Assert.Contains("PNG, ICO, JPEG or WebP", error.Message);
    }

    [Fact]
    public async Task DeletingACustomAssetDoesNotDeleteBuiltInBranding()
    {
        var customPath = await storage.SaveFaviconAsync(
            Upload(PngBytes(), "owl.png"),
            TestContext.Current.CancellationToken);

        storage.DeleteCustomAsset(customPath);
        storage.DeleteCustomAsset(BrandingStorage.DefaultFaviconPath);

        Assert.False(File.Exists(ToPhysicalPath(customPath)));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private string ToPhysicalPath(string publicPath) =>
        Path.Combine(root, "wwwroot", publicPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private static FormFile Upload(byte[] bytes, string name) =>
        new(new MemoryStream(bytes), 0, bytes.Length, "image", name);

    private static byte[] PngBytes() => [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];

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
