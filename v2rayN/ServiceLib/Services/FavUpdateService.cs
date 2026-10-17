namespace ServiceLib.Services;

/// <summary>
/// Custom build (permanent favorites): update channel that follows the fav-N releases of kookoo2024/v2rayNXX
/// instead of the official v2rayN releases. Config, subscriptions and favorites are kept on upgrade.
/// </summary>
public static class FavUpdateService
{
    private const string _tag = "FavUpdate";
    public const string Repo = "kookoo2024/v2rayNXX";
    private const string ReleasesApi = "https://api.github.com/repos/" + Repo + "/releases?per_page=30";
    private const string AssetName = "v2rayN-fav-windows-64.zip";

    public record FavRelease(int Build, string Tag, string DownloadUrl, string Notes);

    /// <summary>Build number of this install (written to fav-version.txt by the build workflow), 0 if unknown.</summary>
    public static int LocalBuild
    {
        get
        {
            try
            {
                var file = Utils.GetBaseDirectory("fav-version.txt");
                if (File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out var n))
                {
                    return n;
                }
            }
            catch
            {
            }
            return 0;
        }
    }

    public static string LocalVersionText => LocalBuild > 0 ? $"fav-{LocalBuild}" : "未知版本";

    /// <summary>Latest fav-N release, or null when it cannot be fetched.</summary>
    public static async Task<FavRelease?> GetLatestAsync()
    {
        var downloader = new DownloadService();
        string? json = null;
        foreach (var blProxy in new[] { true, false })
        {
            try
            {
                json = await downloader.TryDownloadString(ReleasesApi, blProxy, Global.AppName);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
            if (json.IsNotEmpty())
            {
                break;
            }
        }
        if (json.IsNullOrEmpty())
        {
            return null;
        }

        var releases = JsonUtils.Deserialize<List<GitHubRelease>>(json);
        return releases?
            .Where(r => !r.Draft && r.TagName?.StartsWith("fav-", StringComparison.OrdinalIgnoreCase) == true)
            .Select(r =>
            {
                int.TryParse(r.TagName![4..], out var n);
                var url = r.Assets?.FirstOrDefault(a => a.Name == AssetName)?.BrowserDownloadUrl;
                return new FavRelease(n, r.TagName, url ?? string.Empty, r.Body ?? string.Empty);
            })
            .Where(r => r.Build > 0 && r.DownloadUrl.IsNotEmpty())
            .MaxBy(r => r.Build);
    }

    /// <summary>Returns the release when it is newer than this install, otherwise null.</summary>
    public static async Task<FavRelease?> CheckNewAsync()
    {
        var latest = await GetLatestAsync();
        return latest != null && latest.Build > LocalBuild ? latest : null;
    }

    /// <summary>Download the release zip. Returns the local file path, or null on failure.</summary>
    public static async Task<string?> DownloadAsync(FavRelease release, Func<string, Task> progress)
    {
        foreach (var blProxy in new[] { true, false })
        {
            var fileName = Utils.GetTempPath($"{Utils.GetGuid()}.zip");
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var downloader = new DownloadService();
            var lastReport = DateTime.MinValue;
            downloader.UpdateCompleted += (_, args) =>
            {
                if (args.Success)
                {
                    tcs.TrySetResult(true);
                }
                else if (DateTime.Now - lastReport > TimeSpan.FromSeconds(1))
                {
                    lastReport = DateTime.Now;
                    _ = progress(args.Msg ?? string.Empty);
                }
            };
            downloader.Error += (_, args) =>
            {
                _ = progress(args.GetException().Message);
                tcs.TrySetResult(false);
            };

            await progress(blProxy ? "通过代理下载新版本…" : "直连下载新版本…");
            var task = downloader.DownloadFileAsync(new() { FileUrl = release.DownloadUrl, FilePath = fileName }, blProxy);
            await task;
            var ok = tcs.Task.IsCompleted ? tcs.Task.Result : File.Exists(fileName) && new FileInfo(fileName).Length > 1024 * 1024;
            if (ok && File.Exists(fileName))
            {
                return fileName;
            }
        }
        return null;
    }

    /// <summary>Hand the zip to the upgrade tool (AmazTool) and exit. Returns false if it could not start.</summary>
    public static async Task<bool> UpgradeAsync(string zipFile)
    {
        if (!Utils.UpgradeAppExists(out var upgradeFileName))
        {
            return false;
        }
        var id = ProcUtils.ProcessStart(upgradeFileName, zipFile, Utils.StartupPath());
        if (id > 0)
        {
            await AppManager.Instance.AppExitAsync(true);
            return true;
        }
        return false;
    }
}
