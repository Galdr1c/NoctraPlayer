namespace Noctra.Services;

/// <summary>
/// Geliştirme ortamında .env dosyasındaki değişkenleri süreç ortamına yükler
/// (zaten ayarlanmış değişkenlerin üzerine yazmaz). Release'de güvenilir
/// değerler build-time AssemblyMetadata'den gelir; .env yalnızca yerel test
/// / geliştirme içindir.
/// </summary>
internal static class EnvFileLoader
{
    private static int _loaded;

    public static void Load()
    {
        if (Interlocked.Exchange(ref _loaded, 1) == 1)
        {
            return;
        }

        try
        {
            var root = AppContext.BaseDirectory;
            // Kök tespiti: repository'deki gerçek solution adı NoctraPlayer.sln.
            // (Yanlış ad kullanmak döngünün yalnızca .env varlığına güvenmesine
            // yol açar; .env repo kökünde değilse üst dizinlerde yanlış bir .env
            // yüklenebilirdi.)
            while (!string.IsNullOrEmpty(root) &&
                   !File.Exists(Path.Combine(root, ".env")) &&
                   !File.Exists(Path.Combine(root, "NoctraPlayer.sln")))
            {
                root = Path.GetDirectoryName(root);
            }

            var envPath = Path.Combine(root ?? string.Empty, ".env");
            if (!File.Exists(envPath))
            {
                return;
            }

            foreach (var line in File.ReadAllLines(envPath))
            {
                var parts = line.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                var key = parts[0].Trim();
                var value = parts[1].Trim();
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load .env file: {ex.Message}");
        }
    }
}
