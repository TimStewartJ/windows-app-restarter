using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace WindowsAppRestarter;

internal sealed record AvailableUpdate(Version Version, Uri InstallerUri, Uri ChecksumsUri);

/// <summary>
/// Silent background updater backed by GitHub Releases. Discovers the latest tag via the
/// <c>releases/latest</c> redirect, downloads the installer from a URL built from that tag, verifies it
/// against the release's <c>SHA256SUMS.txt</c>, then hands off to Inno Setup.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private const string RepositoryOwner = "TimStewartJ";
    private const string RepositoryName = "windows-app-restarter";
    private const string InstallerFileName = "WindowsAppRestarterSetup.exe";
    private const string ChecksumsFileName = "SHA256SUMS.txt";
    private const long MaxInstallerBytes = 400L * 1024 * 1024;
    private static readonly Uri LatestReleaseUri = new($"https://github.com/{RepositoryOwner}/{RepositoryName}/releases/latest");

    private readonly HttpClient httpClient;
    private readonly HttpClient redirectProbeClient;

    public UpdateService(Version currentVersion)
    {
        CurrentVersion = Normalize(currentVersion);

        var userAgent = $"WindowsAppRestarter/{CurrentVersion} (+https://github.com/{RepositoryOwner}/{RepositoryName})";

        httpClient = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 })
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        redirectProbeClient = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        redirectProbeClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public Version CurrentVersion { get; }

    public static string UpdatesDirectory => Path.Combine(AppLogger.LogDirectory, "updates");

    /// <summary>
    /// Auto-update only makes sense for installer-based installs; portable copies and dev builds are left alone.
    /// </summary>
    public static bool IsInstallerManagedInstall()
    {
        var directory = Path.GetDirectoryName(Environment.ProcessPath);
        return directory is not null && File.Exists(Path.Combine(directory, "unins000.exe"));
    }

    public async Task<AvailableUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, LatestReleaseUri);
        using var response = await redirectProbeClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.MovedPermanently or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
        {
            AppLogger.Info($"Update check: unexpected response {(int)response.StatusCode} from GitHub; assuming no update.");
            return null;
        }

        var location = response.Headers.Location;
        if (location is null)
        {
            return null;
        }

        var resolved = location.IsAbsoluteUri ? location : new Uri(LatestReleaseUri, location);
        var tag = resolved.Segments.LastOrDefault()?.Trim('/');
        if (string.IsNullOrEmpty(tag) || !TryParseTag(tag, out var latest))
        {
            AppLogger.Info($"Update check: could not read a version from '{resolved}'.");
            return null;
        }

        if (latest <= CurrentVersion)
        {
            return null;
        }

        var downloadBase = new Uri($"https://github.com/{RepositoryOwner}/{RepositoryName}/releases/download/{tag}/");
        return new AvailableUpdate(latest, new Uri(downloadBase, InstallerFileName), new Uri(downloadBase, ChecksumsFileName));
    }

    /// <summary>Downloads the installer for <paramref name="update"/> and returns its path once its SHA-256 matches.</summary>
    public async Task<string> DownloadAndVerifyAsync(AvailableUpdate update, CancellationToken cancellationToken)
    {
        var expectedHash = await FetchExpectedHashAsync(update.ChecksumsUri, cancellationToken)
            ?? throw new InvalidOperationException($"Release {update.Version} does not publish a SHA-256 for {InstallerFileName}; refusing to install it.");

        var directory = Path.Combine(UpdatesDirectory, update.Version.ToString(3));
        Directory.CreateDirectory(directory);
        var installerPath = Path.Combine(directory, InstallerFileName);
        var partialPath = installerPath + ".partial";

        using (var response = await httpClient.GetAsync(update.InstallerUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxInstallerBytes)
            {
                throw new InvalidOperationException("The update installer is unexpectedly large; refusing to download it.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            await source.CopyToAsync(target, cancellationToken);
        }

        var actualHash = await ComputeSha256Async(partialPath, cancellationToken);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(partialPath);
            throw new InvalidOperationException($"The downloaded installer failed SHA-256 verification (expected {expectedHash}, got {actualHash}).");
        }

        File.Move(partialPath, installerPath, overwrite: true);
        return installerPath;
    }

    /// <summary>
    /// Starts the silent installer. The caller must exit promptly afterwards; the installer relaunches the app
    /// in background mode when it finishes.
    /// </summary>
    public static void LaunchInstaller(string installerPath, bool startupEnabled)
    {
        var logPath = Path.Combine(Path.GetDirectoryName(installerPath)!, "install.log");
        var tasks = startupEnabled ? "startup" : "!startup";
        var arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /MERGETASKS=\"{tasks}\" /RELAUNCH=1 /LOG=\"{logPath}\"";

        Process.Start(new ProcessStartInfo(installerPath, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installerPath)
        });
    }

    /// <summary>Removes leftover installers from previous updates. Files still in use are simply skipped.</summary>
    public static void CleanUpDownloads()
    {
        if (!Directory.Exists(UpdatesDirectory))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(UpdatesDirectory))
        {
            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public void Dispose()
    {
        httpClient.Dispose();
        redirectProbeClient.Dispose();
    }

    private async Task<string?> FetchExpectedHashAsync(Uri checksumsUri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(checksumsUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        foreach (var rawLine in content.Split('\n'))
        {
            // sha256sum format: "<hex>  <name>" or "<hex> *<name>"
            var line = rawLine.Trim();
            if (line.Length < 66)
            {
                continue;
            }

            var hash = line[..64];
            var name = line[64..].TrimStart(' ', '*', '\t');
            if (string.Equals(name, InstallerFileName, StringComparison.OrdinalIgnoreCase) && hash.All(Uri.IsHexDigit))
            {
                return hash;
            }
        }

        return null;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static bool TryParseTag(string tag, out Version version)
    {
        var text = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
        if (Version.TryParse(text, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    private static Version Normalize(Version version) =>
        new(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
