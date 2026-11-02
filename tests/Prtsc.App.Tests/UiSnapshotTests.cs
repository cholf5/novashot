using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prtsc.App.ViewModels;
using Prtsc.App.Views;
using Prtsc.Core.Capture;
using Prtsc.Core.Common;
using SkiaSharp;
using Xunit;

namespace Prtsc.App.Tests;

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
            var dir = Path.Combine(RepoRoot(), "artifacts", "ui");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Prtsc.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    /// <summary>
    /// 只在显式开启时跑：<c>PRTSC_SNAPSHOT=1 dotnet test --filter UiSnapshot</c>。
    /// 它会 SetupWithoutStarting 一个全局 Avalonia 实例，和 HeadlessUnitTestSession 共用进程会互相踩，
    /// 所以默认不参与常规测试运行。
    /// </summary>
    [Fact]
    public void Capture_Snapshots()
    {
        if (Environment.GetEnvironmentVariable("PRTSC_SNAPSHOT") != "1") return;

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
            vm.EnterEditor(new CapturedImage(MakeFakeShot(), new Prtsc.Core.Common.PixelRect(0, 0, 1400, 900),
                1.0, CaptureSource.Fullscreen));
        }

        Pump(window);
        Save(window, "02-editor-en.png");

        var canvasTab = window.FindControl<Avalonia.Controls.Primitives.ToggleButton>("CanvasTab");
        if (canvasTab is not null)
        {
            canvasTab.IsChecked = true;
            window.FindControl<Avalonia.Controls.Primitives.ToggleButton>("StyleTab")!.IsChecked = false;
            window.FindControl<ScrollViewer>("StyleHost")!.IsVisible = false;
            window.FindControl<ScrollViewer>("CanvasHost")!.IsVisible = true;
        }

        Pump(window);
        Save(window, "03-editor-canvas-tab-en.png");

        Prtsc.App.Localization.Loc.SetPreference("zh-CN", Application.Current!);
        if (window.DataContext is MainWindowViewModel vm2) vm2.ReturnToCapture();
        Pump(window);
        Save(window, "04-capture-zh.png");
        window.Close();

        // 面板特写
        var editor = new EditorViewModel(new CapturedImage(MakeFakeShot(),
            new Prtsc.Core.Common.PixelRect(0, 0, 1400, 900), 1.0, CaptureSource.Fullscreen));

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
        var libDir = Path.Combine(Path.GetTempPath(), "prtsc-lib-" + Guid.NewGuid().ToString("N"));
        var thumbsDir = Path.Combine(libDir, "thumbs");
        Directory.CreateDirectory(thumbsDir);
        var store = new Prtsc.Core.Library.LibraryStore(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Prtsc.Core.Library.LibraryStore>.Instance,
            overrideDirectory: libDir);
        for (var i = 0; i < 3; i++)
        {
            var entry = Task.Run(() => store.AddAsync(new Prtsc.Core.Library.LibraryEntry
            {
                CreatedAt = DateTimeOffset.Now.AddMinutes(-12 * i),
                FileName = $"prtsc-20261102-09{42 - i}13.png",
                LocalPath = "/tmp/shot.png",
                Width = 1400,
                Height = 900,
                SizeBytes = 420_000 + i * 90_000,
                RemoteUrl = i == 0 ? "https://img.example.com/a.png" : null,
                Provider = i == 0 ? "r2" : null,
            })).GetAwaiter().GetResult();

            using var thumb = MakeFakeShot().Resize(new SKImageInfo(240, 150), SKFilterQuality.High);
            Prtsc.Core.Library.ThumbnailWriter.Write(thumbsDir, $"thumb_{entry.Id}.png", thumb);
        }

        var lib = new LibraryWindow(store, thumbsDir, Prtsc.App.AppServices.GetRequiredService<Prtsc.Core.Clipboard.IClipboardService>());
        lib.Show();
        Pump(lib);
        Save(lib, "20-library.png");
        lib.Close();

        // Cloud settings
        var cloud = new CloudSettingsWindow(Prtsc.App.AppServices.GetRequiredService<Prtsc.Core.Config.IConfigStore>());
        cloud.Show();
        Pump(cloud);
        Save(cloud, "21-cloud.png");
        cloud.Close();

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
        var dir = Path.Combine(Path.GetTempPath(), "prtsc-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Prtsc.App.AppServices.Initialize(dir);
        Prtsc.App.Localization.Loc.Initialize("en", Application.Current!);
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
        frame.Save(path);
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

/// <summary>快照宿主 App：真主题（Fluent + Prtsc 自定义样式 + token/图标资源）。</summary>
public class SnapshotBootstrap : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Prtsc/"))
        {
            Source = new Uri("avares://Prtsc/Themes/Controls.axaml"),
        });

        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Prtsc/"))
        {
            Source = new Uri("avares://Prtsc/Themes/Tokens.axaml"),
        });
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Prtsc/"))
        {
            Source = new Uri("avares://Prtsc/Themes/Icons.axaml"),
        });

        RequestedThemeVariant = ThemeVariant.Dark;
    }
}

internal static class AppSnapshotTestAccess
{
    // 占位：避免 partial App 需要改动生产代码
}
