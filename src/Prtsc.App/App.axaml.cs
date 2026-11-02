using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Prtsc.App.Assets;
using Prtsc.App.Logging;
using Prtsc.App.Views;
using Prtsc.Core.Autostart;
using Prtsc.Core.Config;
using Prtsc.Core.Hotkeys;

namespace Prtsc.App;

public partial class App : Application
{
    private TrayIcon? _tray;
    private MainWindow? _mainWindow;
    private IHotkeyService? _hotkeys;
    private ILogger<App>? _logger;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _logger = AppServices.GetRequiredService<ILogger<App>>();

            // 同步 config：先 Load 一次
            var store = AppServices.GetRequiredService<IConfigStore>();
            // macOS：Avalonia 主线程直接 GetAwaiter().GetResult() 会与异步链死锁（2026-11-01 实证）；
            // 包进 Task.Run 打破 SynchronizationContext 依赖，Windows 行为不变。
            Task.Run(() => store.LoadAsync()).GetAwaiter().GetResult();

            // i18n：先解析语言并写入资源字典，后面所有窗口的 DynamicResource 才拿得到文案
            Localization.Loc.Initialize(store.Current.Language, this);
            Localization.Loc.LanguageChanged += (_, _) =>
            {
                RebuildTrayMenu();
                Dispatcher.UIThread.Post(RefreshWindowTitles);
            };

            // S-1：设置保存后的统一副作用——热键重注册 + 语言全局刷新（Update 在 UI 线程同步触发 Changed）
            store.Changed += (_, cfg) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ReapplyHotkeys(cfg.Hotkeys);
                    if (!string.Equals(Localization.Loc.Preference, cfg.Language, StringComparison.OrdinalIgnoreCase)
                        && cfg.Language is "system" or "en" or "zh-CN")
                    {
                        Localization.Loc.SetPreference(cfg.Language, this);
                    }
                });
            };

            // Autostart 路径同步（用户可能移动 exe 位置）
            AppServices.GetRequiredService<IAutostartService>().SyncPath();

            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // G-13：MinimizeToTray=true 时启动即隐藏到托盘（热键/托盘仍可用）
            if (store.Current.Startup.MinimizeToTray)
                _mainWindow.Hide();

            SetupTrayIcon();
            SetupHotkeys(store.Current.Hotkeys);

            desktop.Exit += (_, _) =>
            {
                _hotkeys?.Dispose();
                _tray?.Dispose();
                LoggingSetup.Close();
                AppServices.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon()
    {
        _tray = new TrayIcon
        {
            Icon = AppIconFactory.CreatePlaceholderWindowIcon(),
            // G-19（winshot）：tooltip 带版本号
            ToolTipText = $"Prtsc v{GetAppVersion()}",
            IsVisible = true,
        };

        RebuildTrayMenu();
        _tray.Clicked += (_, _) => ShowMainWindow();
    }

    /// <summary>
    /// 托盘菜单整表重建（切语言时也要重建：NativeMenuItem.Text 是普通字符串，不会跟着资源走）。
    /// </summary>
    private void RebuildTrayMenu()
    {
        if (_tray is null) return;

        var menu = new NativeMenu();

        var showItem = new NativeMenuItem(L("Tray.Show"));
        showItem.Click += (_, _) => ShowMainWindow();
        menu.Add(showItem);

        menu.Add(new NativeMenuItemSeparator());

        var fullscreenItem = new NativeMenuItem(L("Tray.CaptureFullscreen"));
        fullscreenItem.Click += async (_, _) => await TriggerCaptureAsync(HotkeyAction.Fullscreen);
        menu.Add(fullscreenItem);

        var regionItem = new NativeMenuItem(L("Tray.CaptureRegion"));
        regionItem.Click += async (_, _) => await TriggerCaptureAsync(HotkeyAction.Region);
        menu.Add(regionItem);

        var windowItem = new NativeMenuItem(L("Tray.CaptureWindow"));
        windowItem.Click += async (_, _) => await TriggerCaptureAsync(HotkeyAction.Window);
        menu.Add(windowItem);

        menu.Add(new NativeMenuItemSeparator());

        // M7b: Library / 云设置入口
        var libraryItem = new NativeMenuItem(L("Tray.Library"));
        libraryItem.Click += (_, _) =>
        {
            if (_mainWindow is { } main)
                Dispatcher.UIThread.Post(main.ShowLibrary);
        };
        menu.Add(libraryItem);

        var cloudSettingsItem = new NativeMenuItem(L("Tray.CloudSettings"));
        cloudSettingsItem.Click += (_, _) =>
        {
            if (_mainWindow is { } main)
                Dispatcher.UIThread.Post(() =>
                    new CloudSettingsWindow(AppServices.GetRequiredService<IConfigStore>()).Show(main));
        };
        menu.Add(cloudSettingsItem);

        menu.Add(new NativeMenuItemSeparator());

        // S-1：设置中心（语言入口从托盘搬进设置，radio 勾选态风险一并消除）
        var settingsItem = new NativeMenuItem(L("Tray.Settings"));
        settingsItem.Click += (_, _) =>
        {
            if (_mainWindow is { } main)
                Dispatcher.UIThread.Post(() =>
                    new SettingsWindow(AppServices.GetRequiredService<IConfigStore>()).Show(main));
        };
        menu.Add(settingsItem);

        menu.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem(L("Tray.Quit"));
        quitItem.Click += (_, _) => Shutdown();
        menu.Add(quitItem);

        _tray.Menu = menu;
    }

    /// <summary>窗口标题是普通 string 属性，切语言后手动同步一遍。</summary>
    private void RefreshWindowTitles()
    {
        if (_mainWindow is { } main)
            main.ApplyLocalizedTitle();
    }

    private static string L(string key) => Localization.Loc.T("" + key);

    private void SetupHotkeys(HotkeyConfig cfg)
    {
        _hotkeys = AppServices.GetRequiredService<IHotkeyService>();
        _hotkeys.HotkeyPressed += async (_, action) => await TriggerCaptureAsync(action);
        try
        {
            _hotkeys.Start();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start hotkey pump");
        }

        ReapplyHotkeys(cfg);
    }

    /// <summary>S-1：热键配置变更后重注册（注册失败仅记日志，泵不停；现有动作继续用旧键）。</summary>
    private void ReapplyHotkeys(HotkeyConfig cfg)
    {
        if (_hotkeys is null) return;
        try
        {
            _hotkeys.RegisterAll(cfg.Fullscreen, cfg.Region, cfg.Window);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to register global hotkeys");
        }
    }

    private void ShowMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow == null) return;
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    private async Task TriggerCaptureAsync(HotkeyAction action)
    {
        if (_mainWindow == null) return;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            switch (action)
            {
                case HotkeyAction.Fullscreen:
                    await _mainWindow.CaptureFullscreenAsync();
                    break;
                case HotkeyAction.Region:
                    await _mainWindow.CaptureRegionAsync();
                    break;
                case HotkeyAction.Window:
                    await _mainWindow.CaptureWindowAsync();
                    break;
            }
        });
    }

    private void Shutdown()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    /// <summary>程序集 InformationalVersion（无则回退文件版本、再回退 "0.1"）。</summary>
    private static string GetAppVersion()
    {
        var asm = typeof(App).Assembly;
        var info = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info)) return info!;
        return asm.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.1";
    }
}
