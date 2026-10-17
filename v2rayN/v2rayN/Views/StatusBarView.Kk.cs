using System.Windows.Controls;

namespace v2rayN.Views;

/// <summary>
/// Custom build (permanent favorites): switch to a favorite node straight from the tray menu.
/// </summary>
public partial class StatusBarView
{
    private bool _favMenuLoading;

    private async void MenuFavorites_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != menuFavorites || _favMenuLoading)
        {
            return;
        }
        _favMenuLoading = true;
        try
        {
            var favs = await AppManager.Instance.ProfileItems(ConfigHandler.FavSubId) ?? [];
            var exs = (await ProfileExManager.Instance.GetProfileExs())
                .GroupBy(t => t.IndexId)
                .ToDictionary(g => g.Key, g => g.First());

            menuFavorites.Items.Clear();
            if (favs.Count == 0)
            {
                menuFavorites.Items.Add(new MenuItem { Header = "（还没有收藏）", IsEnabled = false });
                return;
            }

            foreach (var fav in favs.OrderBy(t => exs.TryGetValue(t.IndexId, out var x) ? x.Sort : 0))
            {
                exs.TryGetValue(fav.IndexId, out var ex);
                var delay = ex?.Delay ?? 0;
                var delayText = delay > 0 ? $"{delay} ms" : delay < 0 ? "超时" : "未测";
                var item = new MenuItem
                {
                    Header = $"{fav.Remarks}    {delayText}",
                    IsChecked = fav.IndexId == _config.IndexId,
                    Tag = fav.IndexId,
                };
                item.Click += (_, _) =>
                {
                    var id = item.Tag as string;
                    if (id.IsNotEmpty() && id != _config.IndexId)
                    {
                        ViewModel?.SetDefaultServerRequested.Publish(id);
                    }
                };
                menuFavorites.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("TrayFavorites", ex);
        }
        finally
        {
            _favMenuLoading = false;
        }
    }
}
