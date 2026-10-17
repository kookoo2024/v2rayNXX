namespace ServiceLib.Common;

/// <summary>
/// Custom build (permanent favorites) options, stored in guiConfigs/kk_options.txt as key=value lines.
/// </summary>
public static class KkOptions
{
    private static readonly string _file = Utils.GetConfigPath("kk_options.txt");
    private static bool _loaded;

    /// <summary>Show nodes whose delay test failed (-1). Default: hidden.</summary>
    public static bool ShowFailed { get; set; }

    /// <summary>Periodically test favorites and auto switch to the fastest one when the current node is down. Default: on.</summary>
    public static bool AutoSwitch { get; set; } = true;

    public static void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        try
        {
            if (!File.Exists(_file))
            {
                return;
            }
            foreach (var line in File.ReadAllLines(_file))
            {
                var kv = line.Split('=', 2);
                if (kv.Length != 2)
                {
                    continue;
                }
                var val = kv[1].Trim() == "1";
                switch (kv[0].Trim())
                {
                    case nameof(ShowFailed):
                        ShowFailed = val;
                        break;

                    case nameof(AutoSwitch):
                        AutoSwitch = val;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("KkOptions", ex);
        }
    }

    public static void Save()
    {
        try
        {
            File.WriteAllText(_file,
                $"{nameof(ShowFailed)}={(ShowFailed ? 1 : 0)}\n{nameof(AutoSwitch)}={(AutoSwitch ? 1 : 0)}\n");
        }
        catch (Exception ex)
        {
            Logging.SaveLog("KkOptions", ex);
        }
    }
}
