using System.Text.Json;

namespace OwlCTF.Tests;

public sealed class DeploymentConfigurationTests
{
    [Fact]
    public void BaseSettingsHaveNoCommittedSecretsAndAllowLocalProbes()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "src", "OwlCTF.Web", "appsettings.json");
        var text = File.ReadAllText(path);
        using var json = JsonDocument.Parse(text);

        Assert.Equal("*", json.RootElement.GetProperty("AllowedHosts").GetString());
        Assert.DoesNotContain("change-me", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CHANGE_WITH", text, StringComparison.OrdinalIgnoreCase);
        Assert.False(json.RootElement.TryGetProperty("ConnectionStrings", out _));
        Assert.False(json.RootElement.TryGetProperty("Security", out _));

        var productionExample = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OwlCTF.Web",
            "appsettings.Production.example.json"));
        Assert.DoesNotContain("Password=", productionExample, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("use-a-secret", productionExample, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComposeAllowsPublicAndInternalWebHosts()
    {
        var root = FindRepositoryRoot();
        var compose = File.ReadAllText(Path.Combine(root, "compose.yaml"));
        var dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));

        Assert.Contains("AllowedHosts: \"${CTF_HOST:?Set CTF_HOST to the public domain};localhost;127.0.0.1;web\"", compose, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:8080/health/ready", dockerfile, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OwlCTF.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the OwlCTF repository root.");
    }
}
