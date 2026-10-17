namespace ServiceLib.ViewModels;

/// <summary>
/// Custom build (permanent favorites):
/// 1. Hidden failed (-1) nodes can be shown again, and are retested by "test all" so recovered ones come back.
/// 2. Favorites health check: every 30 minutes the favorites are retested; when the current node fails twice
///    in a row (checked every minute), switch to the fastest available favorite.
/// </summary>
public partial class ProfilesViewModel
{
    private const string KkTag = "FavGuard";
    private readonly HashSet<string> _hiddenTestIds = [];
    private bool _hiddenRecovered;
    private volatile bool _userTestRunning;
    private volatile bool _guardTesting;
    private int _guardFailCount;
    private DateTime _lastFavCheck = DateTime.Now;
    private DateTime _lastNoFavNotice = DateTime.MinValue;
    private bool _guardStarted;

    #region Hidden failed nodes

    private async Task ToggleShowFailed()
    {
        KkOptions.ShowFailed = !KkOptions.ShowFailed;
        KkOptions.Save();
        ShowFailedChecked = KkOptions.ShowFailed;
        await RefreshServers();
        NoticeManager.Instance.Enqueue(KkOptions.ShowFailed ? "已显示失效节点（-1）" : "已隐藏失效节点（-1）");
    }

    private async Task<List<ProfileItem>?> AppendHiddenFailed(List<ProfileItem>? lstSelected)
    {
        _hiddenTestIds.Clear();
        _hiddenRecovered = false;
        if (KkOptions.ShowFailed)
        {
            return lstSelected;
        }
        try
        {
            lstSelected ??= [];
            var visible = lstSelected.Select(t => t.IndexId).ToHashSet();
            var all = await AppManager.Instance.ProfileModels(_config.SubIndexId, _serverFilter) ?? [];
            var exs = (await ProfileExManager.Instance.GetProfileExs()).ToDictionary(t => t.IndexId, t => t.Delay);
            var hidden = all.Where(t => !visible.Contains(t.IndexId)
                                        && exs.TryGetValue(t.IndexId, out var d) && d == -1)
                            .ToList();
            if (hidden.Count == 0)
            {
                return lstSelected;
            }
            foreach (var h in hidden)
            {
                _hiddenTestIds.Add(h.IndexId);
            }
            var extra = JsonUtils.Deserialize<List<ProfileItem>>(JsonUtils.Serialize(hidden)) ?? [];
            lstSelected.AddRange(extra);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(KkTag, ex);
        }
        return lstSelected;
    }

    private async Task WatchUserTest(Task task)
    {
        _userTestRunning = true;
        try
        {
            await task;
        }
        catch
        {
        }
        finally
        {
            _userTestRunning = false;
        }
        if (_hiddenRecovered)
        {
            _hiddenRecovered = false;
            RxSchedulers.MainThreadScheduler.Schedule(() => { _ = RefreshServers(); });
        }
    }

    #endregion Hidden failed nodes

    #region Favorites guard

    private async Task ToggleAutoSwitch()
    {
        KkOptions.AutoSwitch = !KkOptions.AutoSwitch;
        KkOptions.Save();
        AutoSwitchChecked = KkOptions.AutoSwitch;
        _guardFailCount = 0;
        NoticeManager.Instance.Enqueue(KkOptions.AutoSwitch
            ? "已开启：收藏节点自动体检 + 当前节点失效时自动切换"
            : "已关闭收藏节点自动体检/自动切换");
        await Task.CompletedTask;
    }

    private void StartFavGuard()
    {
        if (_guardStarted)
        {
            return;
        }
        _guardStarted = true;
        Task.Run(FavGuardLoop);
    }

    private async Task FavGuardLoop()
    {
        // Give the core time to start
        await Task.Delay(TimeSpan.FromSeconds(90));
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await FavGuardTick();
            }
            catch (Exception ex)
            {
                Logging.SaveLog(KkTag, ex);
            }
        } while (await timer.WaitForNextTickAsync().ConfigureAwait(false));
    }

    private async Task FavGuardTick()
    {
        if (!KkOptions.AutoSwitch || _userTestRunning || _guardTesting || _config.IndexId.IsNullOrEmpty())
        {
            return;
        }

        // 1) Is the current node still working?
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        var proxy = new WebProxy($"socks5://{Global.Loopback}:{port}");
        var delay = await ConnectionHandler.GetRealPingTime(proxy);
        if (delay <= 0)
        {
            await Task.Delay(3000);
            delay = await ConnectionHandler.GetRealPingTime(proxy);
        }
        _guardFailCount = delay > 0 ? 0 : _guardFailCount + 1;

        if (_guardFailCount >= 2)
        {
            _guardFailCount = 0;
            await SwitchToBestFavorite();
            return;
        }

        // 2) Periodic health check of the favorites (keeps their delays fresh)
        if (DateTime.Now - _lastFavCheck >= TimeSpan.FromMinutes(30))
        {
            _lastFavCheck = DateTime.Now;
            await TestFavorites();
        }
    }

    private async Task SwitchToBestFavorite()
    {
        Logging.SaveLog($"{KkTag}: current node failed, testing favorites");
        var results = await TestFavorites();
        _lastFavCheck = DateTime.Now;
        if (results == null)
        {
            return;
        }

        var best = results
            .Where(t => t.Key != _config.IndexId && t.Value > 0)
            .OrderBy(t => t.Value)
            .FirstOrDefault();
        if (best.Key.IsNullOrEmpty())
        {
            if (DateTime.Now - _lastNoFavNotice > TimeSpan.FromMinutes(30))
            {
                _lastNoFavNotice = DateTime.Now;
                NoticeManager.Instance.SendMessageAndEnqueue("当前节点连不上，收藏里暂时也没有可用节点");
            }
            return;
        }

        var item = await AppManager.Instance.GetProfileItem(best.Key);
        var name = item?.Remarks ?? best.Key;
        Logging.SaveLog($"{KkTag}: switch to {name} ({best.Value} ms)");
        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            _ = SetDefaultServer(best.Key);
        });
        NoticeManager.Instance.SendMessageAndEnqueue($"当前节点连不上，已自动切换到收藏节点：{name}（{best.Value} ms）");
    }

    /// <summary>
    /// Real-ping all favorites. Returns IndexId -> delay (ms, -1 failed). Null when there is nothing to test.
    /// </summary>
    private async Task<Dictionary<string, int>?> TestFavorites()
    {
        var favs = (await AppManager.Instance.ProfileItems(ConfigHandler.FavSubId))?
            .Where(t => !t.ConfigType.IsGroupType() && t.ConfigType != EConfigType.Custom)
            .ToList();
        if (favs is not { Count: > 0 })
        {
            return null;
        }

        var results = new ConcurrentDictionary<string, int>();
        _guardTesting = true;
        try
        {
            var service = new SpeedtestService(_config, async (SpeedTestResult result) =>
            {
                if (result.IndexId.IsNullOrEmpty())
                {
                    return;
                }
                if (int.TryParse(result.Delay, out var d) && d != 0)
                {
                    results[result.IndexId] = d;
                }
                // Mirror results into the list when the user is looking at it
                RxSchedulers.MainThreadScheduler.Schedule(() =>
                {
                    _ = SetSpeedTestResult(result);
                });
                await Task.CompletedTask;
            });
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await service.RunLoop(ESpeedActionType.Realping, favs, cts.Token);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(KkTag, ex);
        }
        finally
        {
            _guardTesting = false;
        }
        return results.ToDictionary(t => t.Key, t => t.Value);
    }

    #endregion Favorites guard
}
