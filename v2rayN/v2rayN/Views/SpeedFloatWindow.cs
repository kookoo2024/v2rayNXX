using System.Windows.Controls;
using System.Windows.Media;

namespace v2rayN.Views;

/// <summary>
/// Custom build: a small always-on-top floating window that shows the real-time
/// proxy upload/download speed of the current node, placed above the taskbar.
/// Drag to move; position and visibility are remembered.
/// </summary>
public class SpeedFloatWindow : Window
{
    private static SpeedFloatWindow? _instance;
    private static readonly string _stateFile = Utils.GetConfigPath("speedfloat.txt");

    private readonly TextBlock _txtUp;
    private readonly TextBlock _txtDown;
    private IDisposable? _subscription;

    private SpeedFloatWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "v2rayN speed";

        _txtUp = MakeText(Color.FromRgb(0xFF, 0xB7, 0x4D));
        _txtDown = MakeText(Color.FromRgb(0x81, 0xD4, 0xFA));

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(_txtUp);
        panel.Children.Add(_txtDown);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            Child = panel,
            ToolTip = "v2rayN",
            Cursor = Cursors.SizeAll,
        };

        SetSpeed(0, 0);

        MouseLeftButtonDown += (_, e) =>
        {
            try
            {
                DragMove();
                SaveState(true);
            }
            catch
            {
            }
        };
        MouseDoubleClick += (_, _) => ShowMainWindow();

        _subscription = AppEvents.DispatcherStatisticsRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(update =>
            {
                try
                {
                    SetSpeed(update.ProxyUp, update.ProxyDown);
                }
                catch
                {
                }
            });

        Loaded += (_, _) => PlaceWindow();
        Closed += (_, _) => _subscription?.Dispose();
    }

    private static TextBlock MakeText(Color color)
    {
        return new TextBlock
        {
            Foreground = new SolidColorBrush(color),
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            MinWidth = 96,
        };
    }

    private void SetSpeed(long up, long down)
    {
        _txtUp.Text = $"↑ {Utils.HumanFy(up)}/s";
        _txtDown.Text = $"↓ {Utils.HumanFy(down)}/s";
    }

    private void PlaceWindow()
    {
        var wa = SystemParameters.WorkArea;
        var (_, left, top) = LoadState();
        if (left.HasValue && top.HasValue
            && left.Value >= SystemParameters.VirtualScreenLeft
            && top.Value >= SystemParameters.VirtualScreenTop
            && left.Value < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 20
            && top.Value < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 20)
        {
            Left = left.Value;
            Top = top.Value;
        }
        else
        {
            // default: bottom-right corner, right above the taskbar
            Left = wa.Right - ActualWidth - 12;
            Top = wa.Bottom - ActualHeight - 4;
        }
    }

    private static void ShowMainWindow()
    {
        var main = Application.Current.MainWindow;
        if (main == null)
        {
            return;
        }
        main.Show();
        if (main.WindowState == WindowState.Minimized)
        {
            main.WindowState = WindowState.Normal;
        }
        main.Activate();
    }

    private static (bool visible, double? left, double? top) LoadState()
    {
        try
        {
            if (File.Exists(_stateFile))
            {
                var parts = File.ReadAllText(_stateFile).Split(',');
                var visible = parts.Length < 1 || parts[0].Trim() != "0";
                double? left = parts.Length > 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var l) ? l : null;
                double? top = parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : null;
                return (visible, left, top);
            }
        }
        catch
        {
        }
        return (true, null, null);
    }

    private void SaveState(bool visible)
    {
        try
        {
            File.WriteAllText(_stateFile, string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", visible ? 1 : 0, Left, Top));
        }
        catch
        {
        }
    }

    private static void SaveVisibleOnly(bool visible)
    {
        try
        {
            var (_, left, top) = LoadState();
            File.WriteAllText(_stateFile, string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", visible ? 1 : 0, left ?? double.NaN, top ?? double.NaN));
        }
        catch
        {
        }
    }

    /// <summary>Show on startup unless the user hid it last time.</summary>
    public static void ShowDefault()
    {
        if (_instance != null)
        {
            return;
        }
        var (visible, _, _) = LoadState();
        if (!visible)
        {
            return;
        }
        _instance = new SpeedFloatWindow();
        _instance.Show();
    }

    public static void Toggle()
    {
        if (_instance != null)
        {
            _instance.SaveState(false);
            _instance.Close();
            _instance = null;
            return;
        }
        SaveVisibleOnly(true);
        _instance = new SpeedFloatWindow();
        _instance.Show();
    }

    public static void CloseInstance()
    {
        try
        {
            _instance?.Close();
        }
        catch
        {
        }
        _instance = null;
    }
}
