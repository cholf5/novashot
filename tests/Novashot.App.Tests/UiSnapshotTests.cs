using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Novashot.App.ViewModels;
using Novashot.App.Views;
using Novashot.Core.Capture;
using Novashot.Core.Common;
using Novashot.Core.Editor;
using SkiaSharp;
using Xunit;

namespace Novashot.App.Tests;

/// <summary>
/// 视觉快照：用 headless + Skia 把真实窗口渲染成 PNG，用来人工核对新主题。
/// 不参与断言（只是把图写到 <c>artifacts/ui/</c>），所以不会因为像素差异挂 CI。
/// 运行：<c>dotnet test --filter UiSnapshot</c>
/// </summary>
[Collection("HeadlessUi")]
public class UiSnapshotTests
{
    private static string OutDir
    {
        get
        {
            var dir = Path.Combine(TestRepoRoot.Path, "artifacts", "ui");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// 只在显式开启时跑：<c>NOVASHOT_SNAPSHOT=1 dotnet test --filter UiSnapshot</c>。
    /// 它会 SetupWithoutStarting 一个全局 Avalonia 实例，和 HeadlessUnitTestSession 共用进程会互相踩，
    /// 所以默认不参与常规测试运行。
    /// </summary>
    [Fact]
    public void Capture_Snapshots()
    {
        if (Environment.GetEnvironmentVariable("NOVASHOT_SNAPSHOT") != "1") return;

        AppBuilder.Configure<SnapshotBootstrap>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        InitApp();

        var window = new MainWindow
        {
            Width = 1200,
            Height = 800,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new Avalonia.PixelPoint(0, 0),
        };
        window.Show();
        Pump(window);
        Save(window, "01-capture-en.png");

        if (window.DataContext is MainWindowViewModel vm)
        {
            vm.EnterEditor(new CapturedImage(MakeFakeShot(), new Novashot.Core.Common.PixelRect(0, 0, 1400, 900),
                1.0, CaptureSource.Fullscreen));
        }

        Pump(window);
        Save(window, "02-editor-en.png");

        // U-1：Background=None 时预览必须看得见"这里是透明的"（padding 环 / 圆角外角）
        if (window.DataContext is MainWindowViewModel vmNone && vmNone.Editor is { } editorNone)
        {
            var before = editorNone.CompositionState;
            editorNone.CompositionState = before with { Background = BackgroundKind.None };
            Pump(window);
            Save(window, "05-editor-transparent-bg.png");
            editorNone.CompositionState = before;
            Pump(window);
        }

        Novashot.App.Localization.Loc.SetPreference("zh-CN", Application.Current!);
        if (window.DataContext is MainWindowViewModel vm2) vm2.ReturnToCapture();
        Pump(window);
        Save(window, "04-capture-zh.png");
        window.Close();

        // B-6：工具坞按 Editor.VisibleTools 过滤后的编辑器（Select/Rect/Text/Crop 四组并存的裁法）
        Novashot.App.Localization.Loc.SetPreference("en", Application.Current!);
        Novashot.App.AppServices.GetRequiredService<Novashot.Core.Config.IConfigStore>()
            .Update(c => c.Editor.VisibleTools = new List<string> { "Select", "Rectangle", "Text", "Crop" });
        var filteredWindow = new MainWindow
        {
            Width = 1200,
            Height = 800,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new Avalonia.PixelPoint(0, 0),
        };
        filteredWindow.Show();
        if (filteredWindow.DataContext is MainWindowViewModel vmFiltered)
        {
            vmFiltered.EnterEditor(new CapturedImage(MakeFakeShot(),
                new Novashot.Core.Common.PixelRect(0, 0, 1400, 900), 1.0, CaptureSource.Fullscreen));
        }
        Pump(filteredWindow);
        Save(filteredWindow, "06-editor-dock-filtered.png");
        filteredWindow.Close();
        Novashot.App.AppServices.GetRequiredService<Novashot.Core.Config.IConfigStore>()
            .Update(c => c.Editor.VisibleTools = new List<string>());

        // 面板特写
        var editor = new EditorViewModel(new CapturedImage(MakeFakeShot(),
            new Novashot.Core.Common.PixelRect(0, 0, 1400, 900), 1.0, CaptureSource.Fullscreen));

        var style = new Window
        {
            Width = 264, Height = 620,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#212125")),
            Content = new Controls.StylePanel { DataContext = editor.StylePanel },
        };
        style.Show();
        Pump(style);
        Save(style, "10-panel-style.png");
        style.Close();

        // Library：先塞三条记录 + 缩略图
        var libDir = Path.Combine(Path.GetTempPath(), "novashot-lib-" + Guid.NewGuid().ToString("N"));
        var thumbsDir = Path.Combine(libDir, "thumbs");
        Directory.CreateDirectory(thumbsDir);
        var store = new Novashot.Core.Library.LibraryStore(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Novashot.Core.Library.LibraryStore>.Instance,
            overrideDirectory: libDir);
        for (var i = 0; i < 3; i++)
        {
            // 刻意的同步阻塞（xUnit1031）：本测试是快照生成器，全程在自建的 headless UI 线程上
            // 手工泵 Dispatcher.RunJobs；改成 async 会让续体落到线程池线程，RunJobs 会抛 invalid thread。
            // 无 SynchronizationContext，阻塞不死锁。
#pragma warning disable xUnit1031
            var entry = Task.Run(() => store.AddAsync(new Novashot.Core.Library.LibraryEntry
            {
                CreatedAt = DateTimeOffset.Now.AddMinutes(-12 * i),
                FileName = $"novashot-20261102-09{42 - i}13.png",
                LocalPath = "/tmp/shot.png",
                Width = 1400,
                Height = 900,
                SizeBytes = 420_000 + i * 90_000,
                RemoteUrl = i == 0 ? "https://img.example.com/a.png" : null,
                Provider = i == 0 ? "r2" : null,
            })).GetAwaiter().GetResult();

            // 缩略图缩放：SKSamplingOptions 替换已废弃的 SKFilterQuality（High≈Cubic，语义对齐）
            using var thumb = MakeFakeShot().Resize(new SKImageInfo(240, 150),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
#pragma warning restore xUnit1031
            Novashot.Core.Library.ThumbnailWriter.Write(thumbsDir, $"thumb_{entry.Id}.png", thumb);
        }

        var lib = new LibraryWindow(store, thumbsDir, Novashot.App.AppServices.GetRequiredService<Novashot.Core.Clipboard.IClipboardService>());
        lib.Show();
        Pump(lib);
        Save(lib, "20-library.png");
        lib.Close();

        // Cloud settings
        var cloud = new CloudSettingsWindow(Novashot.App.AppServices.GetRequiredService<Novashot.Core.Config.IConfigStore>());
        cloud.Show();
        Pump(cloud);
        Save(cloud, "21-cloud.png");
        cloud.Close();

        // Settings center (S-1)：默认落 general 分区
        var settings = new SettingsWindow(
            Novashot.App.AppServices.GetRequiredService<Novashot.Core.Config.IConfigStore>(),
            new StubAutostartService());
        settings.Show();
        Pump(settings);
        Save(settings, "30-settings.png");
        settings.Close();

        // Capture Launcher (G-26)：默认区域模式 + 主屏预填
        var launcher = new CaptureLauncherWindow();
        launcher.Show();
        Pump(launcher);
        Save(launcher, "40-launcher.png");
        launcher.Close();

        // E-1 M9 取色器：放大镜 + HEX/RGB 浮签（C 键取色模式下的视觉态；直接驱动放大镜属性，
        // 模式接线由 ColorPickerOverlayTests 锁定，这里只出视觉图人工核对）
        var pickerBmp = MakeFakeShot();
        var pickerDisplay = new Novashot.Core.Common.DisplayInfo(0, "Snapshot",
            new Novashot.Core.Common.PixelRect(0, 0, 1400, 900),
            new Novashot.Core.Common.PixelRect(0, 0, 1400, 900), 1.0, IsPrimary: true);
        var pickerOverlay = new OverlayWindow(pickerBmp, pickerDisplay)
        {
            Width = 700,
            Height = 450,
        };
        var pm = pickerOverlay.FindControl<Controls.ColorMagnifier>("ColorMagnifier");
        if (pm is not null)
        {
            pm.IsVisible = true;
            pm.CursorPoint = new Avalonia.Point(430, 260);
        }
        pickerOverlay.Show();
        Pump(pickerOverlay);
        Save(pickerOverlay, "50-overlay-picker.png");
        pickerOverlay.Close();
        pickerBmp.Dispose();

        var canvas = new Window
        {
            Width = 264, Height = 726,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#212125")),
            Content = new ScrollViewer { Content = new Controls.CompositionPanel { DataContext = editor.CompositionPanelViewModel } },
        };
        canvas.Show();
        Pump(canvas);
        Save(canvas, "11-panel-canvas.png");
        canvas.Close();
        editor.Dispose();
    }

    private static void InitApp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novashot-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Novashot.App.AppServices.Initialize(dir);
        Novashot.App.Localization.Loc.Initialize("en", Application.Current!);
    }

    private static void Pump(Window window)
    {
        for (var i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Thread.Sleep(30);
        }
    }

    private static void Save(Window window, string fileName)
    {
        var frame = window.CaptureRenderedFrame();
        if (frame is null) return;
        var path = Path.Combine(OutDir, fileName);
        // 显式 PNG 编码选项（Bitmap.Save(string,int?) 已废弃）；快照恒为 PNG，质量参数无意义
        frame.Save(path, PngBitmapEncoderOptions.Default);
        Console.WriteLine($"snapshot → {path}");
    }

    private static SKBitmap MakeFakeShot()
    {
        var bmp = new SKBitmap(1400, 900, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(0xF7, 0xF8, 0xFA));

        using var bar = new SKPaint { Color = new SKColor(0xEC, 0xEF, 0xF3), IsAntialias = true };
        canvas.DrawRect(0, 0, 1400, 56, bar);
        canvas.DrawRect(0, 56, 180, 900, bar);

        using var card = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var line = new SKPaint { Color = new SKColor(0xDD, 0xE2, 0xEA), IsAntialias = true };
        using var accent = new SKPaint { Color = new SKColor(0x8B, 0x5C, 0xF6), IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(240, 120, 1320, 260), 12, 12, card);
        canvas.DrawRoundRect(new SKRect(240, 300, 700, 620), 12, 12, card);
        canvas.DrawRoundRect(new SKRect(740, 300, 1320, 620), 12, 12, card);
        canvas.DrawRoundRect(new SKRect(240, 660, 1320, 840), 12, 12, card);
        canvas.DrawRoundRect(new SKRect(280, 160, 700, 190), 6, 6, line);
        canvas.DrawRoundRect(new SKRect(280, 205, 520, 228), 6, 6, line);
        canvas.DrawRoundRect(new SKRect(780, 340, 900, 372), 6, 6, accent);
        for (var i = 0; i < 5; i++)
            canvas.DrawRoundRect(new SKRect(280, 350 + i * 46, 640 - i * 30, 372 + i * 46), 6, 6, line);

        return bmp;
    }
}

/// <summary>快照宿主 App：真主题（Fluent + Novashot 自定义样式 + token/图标资源）。</summary>
public class SnapshotBootstrap : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Novashot/"))
        {
            Source = new Uri("avares://Novashot/Themes/Controls.axaml"),
        });

        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Novashot/"))
        {
            Source = new Uri("avares://Novashot/Themes/Tokens.axaml"),
        });
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Novashot/"))
        {
            Source = new Uri("avares://Novashot/Themes/Icons.axaml"),
        });

        RequestedThemeVariant = ThemeVariant.Dark;
    }
}

internal static class AppSnapshotTestAccess
{
    // 占位：避免 partial App 需要改动生产代码
}
