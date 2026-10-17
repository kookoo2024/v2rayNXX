using ServiceLib.Services;

namespace v2rayN.Views;

/// <summary>
/// Custom build (permanent favorites): own update channel (fav-N releases of kookoo2024/v2rayNXX).
/// </summary>
public partial class MainWindow
{
    private bool _favUpdating;

    private void StartFavUpdateCheck()
    {
        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(60));
            while (true)
            {
                try
                {
                    var rel = await FavUpdateService.CheckNewAsync();
                    if (rel != null)
                    {
                        NoticeManager.Instance.SendMessageAndEnqueue(
                            $"定制版有新版本 {rel.Tag}（当前 {FavUpdateService.LocalVersionText}），可在「设置 → 检查定制版更新」一键更新");
                    }
                }
                catch (Exception ex)
                {
                    Logging.SaveLog("FavUpdate", ex);
                }
                await Task.Delay(TimeSpan.FromHours(24));
            }
        });
    }

    private async void MenuFavUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_favUpdating)
        {
            NoticeManager.Instance.Enqueue("正在检查/下载更新，请稍候…");
            return;
        }
        _favUpdating = true;
        try
        {
            NoticeManager.Instance.Enqueue("正在检查定制版更新…");
            var latest = await Task.Run(FavUpdateService.GetLatestAsync);
            if (latest == null)
            {
                UI.Show("检查失败：连不上 GitHub，稍后再试。");
                return;
            }
            if (latest.Build <= FavUpdateService.LocalBuild)
            {
                UI.Show($"已是最新版本（{FavUpdateService.LocalVersionText}）。");
                return;
            }

            var notes = latest.Notes.Length > 600 ? latest.Notes[..600] + "…" : latest.Notes;
            var msg = $"发现新版本 {latest.Tag}（当前 {FavUpdateService.LocalVersionText}）。\n\n{notes}\n\n现在下载并更新吗？\n配置、订阅和我的收藏都会保留，更新完会自动重启。";
            if (UI.ShowYesNo(msg) != MessageBoxResult.Yes)
            {
                return;
            }

            var file = await Task.Run(() => FavUpdateService.DownloadAsync(latest, async m =>
            {
                NoticeManager.Instance.SendMessage(m);
                await Task.CompletedTask;
            }));
            if (file == null)
            {
                UI.Show("下载失败，稍后再试。");
                return;
            }

            NoticeManager.Instance.SendMessage("下载完成，正在更新并重启…");
            if (!await FavUpdateService.UpgradeAsync(file))
            {
                UI.Show($"没找到升级程序 AmazTool.exe，请手动下载解压覆盖：\nhttps://github.com/{FavUpdateService.Repo}/releases/tag/{latest.Tag}");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("FavUpdate", ex);
            UI.Show($"更新出错：{ex.Message}");
        }
        finally
        {
            _favUpdating = false;
        }
    }
}
