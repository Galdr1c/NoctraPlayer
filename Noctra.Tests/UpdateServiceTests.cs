using System.Reflection;
using System.Text.Json;
using Noctra.Models;
using Noctra.Services;

namespace Noctra.Tests;

public class UpdateServiceTests
{
    [Fact]
    public void UpdateManifestUrl_ShouldPointToNoctraPlayerManifest()
    {
        var field = typeof(UpdateService).GetField("UpdateManifestUrl", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.Equal(
            "https://raw.githubusercontent.com/Galdr1c/NoctraPlayer/main/update.json",
            field!.GetRawConstantValue());
    }

    [Fact]
    public async Task UpdateManifestFile_ShouldExistAndDeserialize()
    {
        var manifestPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "update.json"));

        Assert.True(File.Exists(manifestPath), $"Missing update manifest: {manifestPath}");

        var json = await File.ReadAllTextAsync(manifestPath);
        var manifest = JsonSerializer.Deserialize<UpdateInfo>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(manifest);
        Assert.False(string.IsNullOrWhiteSpace(manifest!.Version));
        Assert.False(string.IsNullOrWhiteSpace(manifest.DownloadUrl));
    }
}
