using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace KsyxisTweaks;

internal static class UpdateService
{
    private const string Owner = "vxyaq";
    private const string Repository = "update-install";
    private const string ReleasesApi = $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";

    private static readonly HttpClient Client = CreateHttpClient();
    private static bool _hasChecked;

    public static async Task CheckAndOfferAsync(Window owner)
    {
        if (_hasChecked)
        {
            return;
        }

        _hasChecked = true;

        try
        {
            ReleaseInfo? release = await GetLatestReleaseAsync();
            if (release is null || release.IsPrerelease || !IsNewerVersion(release.TagName))
            {
                return;
            }

            ReleaseAsset? asset = release.SelectInstallerAsset();
            if (asset is null)
            {
                return;
            }

            MessageBoxResult choice = MessageBox.Show(
                owner,
                $"Dostępna jest nowa wersja Ksyxis Tweaks ({release.DisplayVersion}).\n\n" +
                "Czy chcesz pobrać i zainstalować aktualizację teraz?",
                "Ksyxis Tweaks — aktualizacja",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (choice != MessageBoxResult.Yes)
            {
                return;
            }

            string installerPath = await DownloadInstallerAsync(asset);
            StartInstaller(installerPath, asset.IsMsi);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ksyxis Tweaks update check failed: {ex}");
        }
    }

    private static async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        using HttpResponseMessage response = await Client.GetAsync(ReleasesApi);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<ReleaseInfo>(stream);
    }

    private static bool IsNewerVersion(string tagName)
    {
        string remoteVersion = tagName.Trim().TrimStart('v', 'V');
        Version current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
        return Version.TryParse(remoteVersion, out Version? remote) && remote is not null && remote > current;
    }

    private static async Task<string> DownloadInstallerAsync(ReleaseAsset asset)
    {
        string extension = asset.IsMsi ? ".msi" : ".exe";
        string path = Path.Combine(Path.GetTempPath(), $"KsyxisTweaks-update-{Guid.NewGuid():N}{extension}");

        using HttpResponseMessage response = await Client.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using Stream input = await response.Content.ReadAsStreamAsync();
        await using FileStream output = File.Create(path);
        await input.CopyToAsync(output);
        await output.FlushAsync();

        if (asset.Size > 0 && new FileInfo(path).Length != asset.Size)
        {
            File.Delete(path);
            throw new InvalidDataException("Pobrany plik aktualizacji ma nieprawidłowy rozmiar.");
        }

        return path;
    }

    private static void StartInstaller(string installerPath, bool isMsi)
    {
        ProcessStartInfo startInfo = isMsi
            ? new ProcessStartInfo("msiexec.exe", $"/i \"{installerPath}\"")
            : new ProcessStartInfo(installerPath) { Verb = "runas" };

        startInfo.UseShellExecute = true;
        Process.Start(startInfo);
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KsyxisTweaks-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private sealed class ReleaseInfo
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("prerelease")]
        public bool IsPrerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<ReleaseAsset> Assets { get; set; } = new();

        public string DisplayVersion => TagName.TrimStart('v', 'V');

        public ReleaseAsset? SelectInstallerAsset()
        {
            return Assets
                .Where(asset => asset.IsSupportedInstaller)
                .OrderByDescending(asset => asset.IsSetupExecutable)
                .ThenByDescending(asset => asset.IsMsi)
                .FirstOrDefault();
        }
    }

    private sealed class ReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        public bool IsMsi => Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
        public bool IsSetupExecutable => Name.Contains("setup", StringComparison.OrdinalIgnoreCase) || Name.Contains("install", StringComparison.OrdinalIgnoreCase);
        public bool IsSupportedInstaller =>
            (IsMsi || Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) &&
            Name.StartsWith("KsyxisTweaks", StringComparison.OrdinalIgnoreCase) &&
            !Name.Contains("portable", StringComparison.OrdinalIgnoreCase);
    }
}
