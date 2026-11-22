using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Novashot.App.Assets;
using Novashot.App.Cli;
using Novashot.App.Logging;
using Novashot.App.Localization;
using Novashot.App.ViewModels;
using Novashot.App.Views;
using Novashot.App.Windowing;
using Novashot.Core.Autostart;
using Novashot.Core.Capture;
using Novashot.Core.Cli;
using Novashot.Core.Clipboard;
using Novashot.Core.Common;
using Novashot.Core.Config;
using Novashot.Core.Hotkeys;
// 捕获语义的 PixelRect 是 Core 的物理像素矩形；Avalonia.PixelRect 是 DIP 坐标，二者不可混用
using PixelRect = Novashot.Core.Common.PixelRect;

namespace Novashot.App;

public partial class App : Application
{
    private TrayIcon? _tray;
    private MainWindow? _mainWindow;
    private IHotkeyService? _hotkeys;
    private HotkeyConfig? _lastHotkeys; // 同规格不重注册（C1 防线）：关窗写尺寸等 Update 也会走 Changed
    private ILogger<App>? _logger;
    private IConfigStore? _store;

    // D-1：延时截图倒计时（Core Orchestrator 持有 pending 意图与轮次裁决；App 只做 marshal 与角标 UI）
    private CountdownOrchestrator<CaptureIntent>? _countdown;
    private CountdownBadgeWindow? _countdownBadge;

    // L-1：CLI 单实例管道服务端（App 运行全程持有，退出时停；accept/handler 全程吞异常不影响 GUI）
    private CliPipeServerHost? _cliPipeHost;

    /// <summary>角标关闭到抓帧的间隙（对齐既有 Hide+Delay(200) 防残影做法，取 250ms）。</summary>
    private const int CountdownToCaptureGapMs = 250;

    /// <summary>D-1 触发来源：热键在倒计时期间 = 取消本轮（验收「可取消」）；托盘/启动器 = 取消旧轮按新意图处理。</summary>
    private enum CaptureTriggerSource { Hotkey, Tray, Launcher }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // B-9：深色标题栏钩子必须在任何窗口 Show 之前注册（主窗在下方创建）
            CaptionTheme.AttachAll();

            _logger = AppServices.GetRequiredService<ILogger<App>>();

            // 同步 config：先 Load 一次
            var store = AppServices.GetRequiredService<IConfigStore>();
            _store = store;
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

            // L-1：CLI 单实例管道服务端（启动即开、退出即停；接收分发见 BuildCliPipeRouter）
            StartCliPipeHost();

            // C-1/R-1：启动更新检查（fire-and-forget）——policy 拒绝/未打包/无 feed/无网络全都静默，
            // 有更新才弹非阻塞提示框；内部所有 Velopack 调用自带 try-catch，绝不阻塞启动路径
            _ = Update.StartupUpdateChecker.RunOnStartupAsync(store, _mainWindow, _logger);

            desktop.Exit += (_, _) =>
            {
                // E-2 跟进：退出前把防抖中的 config 改动（画布参数/窗口尺寸）同步落盘，
                // 优雅退出（托盘 Quit / 会话结束）不丢最后 500ms；进程强杀仍可接受丢失
                (_mainWindow?.DataContext as MainWindowViewModel)?.FlushConfigSaveSync();
                _cliPipeHost?.Dispose();
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
            ToolTipText = $"Novashot v{GetAppVersion()}",
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
        fullscreenItem.Click += async (_, _) => await TriggerCaptureAsync(HotkeyAction.Fullscreen, CaptureTriggerSource.Tray);
        menu.Add(fullscreenItem);

        var regionItem = new NativeMenuItem(L("Tray.CaptureRegion"));
        regionItem.Click += async (_, _) => await TriggerCaptureAsync(HotkeyAction.Region, CaptureTriggerSource.Tray);
        menu.Add(regionItem);

        var windowItem = new NativeMenuItem(L("Tray.CaptureWindow"));
        windowItem.Click += async (_, _) => await TriggerCaptureAsync(HotkeyAction.Window, CaptureTriggerSource.Tray);
        menu.Add(windowItem);

        // D-1：延时截图子菜单（走全屏模式；倒计时期间再点 = 取消旧轮重启新倒计时）
        var delayedMenu = new NativeMenu();
        foreach (var seconds in new[] { 3, 5 })
        {
            var delayedItem = new NativeMenuItem(L(seconds == 3 ? "Tray.DelayedCapture.3s" : "Tray.DelayedCapture.5s"));
            var delay = seconds;
            delayedItem.Click += async (_, _) =>
                await TriggerCaptureAsync(HotkeyAction.Fullscreen, CaptureTriggerSource.Tray, delay);
            delayedMenu.Add(delayedItem);
        }
        menu.Add(new NativeMenuItem(L("Tray.DelayedCapture")) { Menu = delayedMenu });

        // G-26：Capture Launcher（flameshot 式独立启动器窗口）
        var launcherItem = new NativeMenuItem(L("Tray.CaptureLauncher"));
        launcherItem.Click += (_, _) => Dispatcher.UIThread.Post(ShowCaptureLauncher);
        menu.Add(launcherItem);

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
        if (cfg.SameSpecsAs(_lastHotkeys)) return; // 未变不重注册：Windows 泵侧重注册有真实成本，只在真变更时走
        try
        {
            _hotkeys.RegisterAll(cfg.Fullscreen, cfg.Region, cfg.Window);
            _lastHotkeys = new HotkeyConfig { Fullscreen = cfg.Fullscreen, Region = cfg.Region, Window = cfg.Window };
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

    /// <summary>
    /// D-1 咽喉点：全部捕获触发（热键 / 托盘 / Capture Launcher）都汇到这里。
    /// 延时解析：显式 delaySeconds（托盘延时子项、Launcher）优先，否则取 config 默认（钳制后）。
    /// 倒计时语义：进行中再触发——热键 = 取消本轮不启动新一轮（验收「可取消」）；
    /// 托盘/启动器 = 取消旧轮 + 按新意图处理（延时刻意选 0 的 Launcher 会直接抓帧）。
    /// </summary>
    private async Task TriggerCaptureAsync(HotkeyAction action,
        CaptureTriggerSource source = CaptureTriggerSource.Hotkey,
        int? delaySeconds = null,
        PixelRect? region = null)
    {
        if (_mainWindow == null) return;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // 延时解析（Core 纯函数，review M-2）：显式意图优先，否则 config 默认钳制
            var delay = CaptureDelayOptions.Resolve(delaySeconds,
                _store?.Current.Capture.DelaySeconds ?? 0);

            if (_countdown is { IsRunning: true })
            {
                _countdown.Cancel(); // 停状态机 + 清 pending + 关角标
                if (source == CaptureTriggerSource.Hotkey)
                {
                    // 唯一明确的取消手段（角标上有提示文案），本轮到此为止
                    _mainWindow.ShowStatus(Loc.T("Status.DelayCanceled"));
                    return;
                }
            }
            else
            {
                _countdown?.ClearPending(); // 清残留意图（finisher 尚在 250ms settle 间隙时的新触发；轮次裁决兜底）
            }

            if (delay > 0)
            {
                EnsureCountdown().Start(new CaptureIntent(action, region), delay);
                return;
            }

            await DispatchCaptureAsync(action, region);
        });
    }

    /// <summary>G-26：Capture Launcher「开始截图」入口（窗口已自关，含关窗 200ms 防残影）。</summary>
    public Task LaunchCaptureAsync(bool fullscreen, PixelRect? region, int delaySeconds)
        => TriggerCaptureAsync(
            fullscreen ? HotkeyAction.Fullscreen : HotkeyAction.Region,
            CaptureTriggerSource.Launcher,
            delaySeconds,
            region);

    /// <summary>按模式分发到 MainWindow 的既有捕获流程（region 仅在 Region 模式下由 Launcher 预置）。</summary>
    private async Task DispatchCaptureAsync(HotkeyAction action, PixelRect? region)
    {
        if (_mainWindow == null) return;
        switch (action)
        {
            case HotkeyAction.Fullscreen:
                await _mainWindow.CaptureFullscreenAsync();
                break;
            case HotkeyAction.Region:
                await (region is { } r
                    ? _mainWindow.CapturePresetRegionAsync(r)
                    : _mainWindow.CaptureRegionAsync());
                break;
            case HotkeyAction.Window:
                await _mainWindow.CaptureWindowAsync();
                break;
        }
    }

    // ---- D-1：倒计时编排 + 角标窗（App 只做线程 marshal 与 UI；pending/轮次/settle 裁决都在 Core Orchestrator） ----

    /// <summary>
    /// 构建倒计时编排器（单实例）。线程接线：Start/Cancel/OnCompletedAsync 全部发生在 UI 线程
    /// （Completed 由下面这行订阅 marshal 过去）；Started 在 Start 调用线程（=UI）同步触发，
    /// Tick 在调度器线程触发故需 Post；dispatch 由 OnCompletedAsync 在 UI 线程调用。
    /// </summary>
    private CountdownOrchestrator<CaptureIntent> EnsureCountdown()
    {
        if (_countdown is not null) return _countdown;
        var countdown = new DelayedCaptureCountdown(CountdownScheduler.Instance);
        var orchestrator = new CountdownOrchestrator<CaptureIntent>(
            countdown,
            intent => _ = DispatchCaptureAsync(intent.Action, intent.Region),
            settleDelay: TimeSpan.FromMilliseconds(CountdownToCaptureGapMs),
            onStarted: ShowCountdownBadge,
            onTick: remainingSeconds =>
                Dispatcher.UIThread.Post(() => _countdownBadge?.UpdateRemaining(remainingSeconds)),
            onBadgeClose: CloseCountdownBadge);
        countdown.Completed += (_, round) =>
            Dispatcher.UIThread.Post(() => _ = orchestrator.OnCompletedAsync(round));
        _countdown = orchestrator;
        return orchestrator;
    }

    private void ShowCountdownBadge(int totalSeconds)
    {
        if (_countdownBadge is null)
        {
            _countdownBadge = new CountdownBadgeWindow();
            _countdownBadge.Closed += (_, _) => _countdownBadge = null;
        }
        _countdownBadge.UpdateRemaining(totalSeconds);
        _countdownBadge.Show();
    }

    private void CloseCountdownBadge()
    {
        _countdownBadge?.Close();
        _countdownBadge = null;
    }

    // ---- G-26：Capture Launcher 窗口（单实例） ----

    private CaptureLauncherWindow? _launcherWindow;

    private void ShowCaptureLauncher()
    {
        if (_launcherWindow is { } existing)
        {
            existing.Activate();
            return;
        }
        _launcherWindow = new CaptureLauncherWindow();
        _launcherWindow.Closed += (_, _) => _launcherWindow = null;
        _launcherWindow.Show();
    }

    private void Shutdown()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    // ---- L-1：CLI 单实例管道服务端（接收转发命令） ----

    private void StartCliPipeHost()
    {
        try
        {
            _cliPipeHost = new CliPipeServerHost(
                CliPipeNames.ForCurrentUser(),
                (command, ct) => BuildCliPipeRouter().HandleAsync(command, ct),
                _logger);
            _cliPipeHost.Start();
        }
        catch (Exception ex)
        {
            // 管道起不来只影响「CLI 转发到 GUI」能力，headless CLI 仍可用——绝不影响 GUI 主流程
            _logger?.LogError(ex, "Failed to start CLI pipe server host");
        }
    }

    /// <summary>
    /// CLI 接收分发（线程契约：本方法跑在管道连接的后台线程——
    /// 交互/激活回调内部自行 marshal 到 UI 线程；headless 分支留在后台线程跑导出执行器，无 GUI 闪现）。
    /// </summary>
    private CliRequestRouter BuildCliPipeRouter() => new(
        activate: ShowMainWindow,
        triggerInteractive: (action, delaySeconds) =>
            _ = TriggerCaptureAsync(action, CaptureTriggerSource.Tray, delaySeconds),
        executeHeadless: async (command, ct) =>
        {
            try
            {
                var executor = new CliCaptureExecutor(
                    AppServices.GetRequiredService<ICaptureService>(),
                    AppServices.GetRequiredService<IConfigStore>(),
                    AppServices.GetRequiredService<IClipboardService>());
                var result = await executor.ExecuteAsync(command, ct).ConfigureAwait(false);
                _logger?.LogInformation("CLI forwarded command executed: {Message}", result.Message);
                return result.ToResponse();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "CLI forwarded command failed ({Mode})", command.Mode);
                return CliPipeResponse.Fail(CliExitCodes.Failure, $"Command handling failed: {ex.Message}");
            }
        });

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

/// <summary>D-1：一轮延时捕获的意图（绑定轮次号挂在 Core CountdownOrchestrator 上，C-1 裁决用）。</summary>
internal readonly record struct CaptureIntent(HotkeyAction Action, PixelRect? Region);
