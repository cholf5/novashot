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

            // Autostart 路径同步（用户可能移动 exe 位置）
            AppServices.GetRequiredService<IAutostartService>().SyncPath();

            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

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
            ToolTipText = "Prtsc",
            IsVisible = true,
        };

        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("Show Prtsc");
        showItem.Click += (_, _) => ShowMainWindow();
        menu.Add(showItem);

        menu.Add(new NativeMenuItemSeparator());

        var fullscreenItem = new NativeMenuItem("Capture Fullscreen");
        fullscreenItem.Click += async (_, _) => await TriggerCaptureAsync(HotkeyAction.Fullscreen);
        menu.Add(fullscreenItem);

        var regionItem = new NativeMenuItem("Capture Region");
        regionItem.Click += async (_, _) => await TriggerCaptureAsync(HotkeyAction.Region);
        menu.Add(regionItem);

        var windowItem = new NativeMenuItem("Capture Window");
        windowItem.Click += async (_, _) => await TriggerCaptureAsync(HotkeyAction.Window);
        menu.Add(windowItem);

        menu.Add(new NativeMenuItemSeparator());

        // M7b: Library / 云设置入口
        var libraryItem = new NativeMenuItem("截图历史…");
        libraryItem.Click += (_, _) =>
        {
            if (_mainWindow is { } main)
                Dispatcher.UIThread.Post(main.ShowLibrary);
        };
        menu.Add(libraryItem);

        var cloudSettingsItem = new NativeMenuItem("云上传设置…");
        cloudSettingsItem.Click += (_, _) =>
        {
            if (_mainWindow is { } main)
                Dispatcher.UIThread.Post(() =>
                    new CloudSettingsWindow(AppServices.GetRequiredService<IConfigStore>()).Show(main));
        };
        menu.Add(cloudSettingsItem);

        menu.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => Shutdown();
        menu.Add(quitItem);

        _tray.Menu = menu;
        _tray.Clicked += (_, _) => ShowMainWindow();
    }

    private void SetupHotkeys(HotkeyConfig cfg)
    {
        _hotkeys = AppServices.GetRequiredService<IHotkeyService>();
        _hotkeys.HotkeyPressed += async (_, action) => await TriggerCaptureAsync(action);
        try
        {
            _hotkeys.Start();
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
}
