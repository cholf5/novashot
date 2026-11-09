using Prtsc.Core.Capture;
using Prtsc.Core.Cli;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Common;
using Prtsc.Core.Config;
using Prtsc.Core.Editor.Export;
using SkiaSharp;

namespace Prtsc.Core.Tests;

/// <summary>
/// L-1 headless 导出执行器全分支（Fake 服务）：full/region 落盘（FilenamePatterns 模式、目录自建）、
/// 剪贴板（快照位图）、--path + --clipboard 并存、格式映射、延时、取消、失败路径、交互命令兜底拒绝。
/// </summary>
public class CliCaptureExecutorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeCaptureService _capture = new();
    private readonly FailInjectableClipboardService _clipboard = new();
    private readonly IConfigStore _store;

    public CliCaptureExecutorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "prtsc-cli-tests", Guid.NewGuid().ToString("N"));
        _store = new JsonConfigStore(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonConfigStore>.Instance,
            overrideDirectory: Path.Combine(_tempDir, "config"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        _capture.DisposeShots();
    }

    private CliCaptureExecutor CreateExecutor(Func<int, CancellationToken, Task>? delay = null) =>
        new(_capture, _store, _clipboard, delay);

    // ---------- full + --path ----------

    [Fact]
    public async Task Full_WithPath_WritesTimestampPatternPng()
    {
        var executor = CreateExecutor();
        var outDir = Path.Combine(_tempDir, "out");

        var result = await executor.ExecuteAsync(new CliCommand
        {
            Mode = CliCaptureMode.Full,
            Path = outDir,
        });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.SavedPath);
        Assert.True(result.FileBytes > 0);
        Assert.False(result.CopiedToClipboard);
        Assert.True(File.Exists(result.SavedPath));
        Assert.Equal(Path.Combine(outDir), Path.GetDirectoryName(result.SavedPath));
        Assert.Matches(@"^prtsc_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.png$", Path.GetFileName(result.SavedPath));
        Assert.Contains("Saved to", result.Message);
        Assert.Contains("bytes", result.Message);
    }

    [Fact]
    public async Task Full_WithPath_CreatesMissingDirectory()
    {
        var executor = CreateExecutor();
        var nested = Path.Combine(_tempDir, "a", "b", "c");

        var result = await executor.ExecuteAsync(new CliCommand { Mode = CliCaptureMode.Full, Path = nested });

        Assert.True(result.Success);
        Assert.True(Directory.Exists(nested));
    }

    [Fact]
    public async Task Full_WithPath_IncrementPattern_FollowsConfig()
    {
        _store.Update(c => c.QuickSave.Pattern = Prtsc.Core.Utils.FilenamePatterns.Increment);
        var executor = CreateExecutor();

        var first = await executor.ExecuteAsync(new CliCommand { Mode = CliCaptureMode.Full, Path = _tempDir });
        var second = await executor.ExecuteAsync(new CliCommand { Mode = CliCaptureMode.Full, Path = _tempDir });

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.EndsWith("prtsc_001.png", Path.GetFileName(first.SavedPath));
        Assert.EndsWith("prtsc_002.png", Path.GetFileName(second.SavedPath));
    }

    // ---------- 格式映射 ----------

    [Fact]
    public async Task Full_WithJpeg_ExtensionAndFormatFollow()
    {
        var executor = CreateExecutor();
        var result = await executor.ExecuteAsync(new CliCommand
        {
            Mode = CliCaptureMode.Full,
            Path = _tempDir,
            Format = ExportFormat.Jpeg,
        });
        Assert.True(result.Success);
        Assert.EndsWith(".jpg", result.SavedPath);
    }

    [Fact]
    public async Task Full_WithWebp_ExtensionFollows()
    {
        var executor = CreateExecutor();
        var result = await executor.ExecuteAsync(new CliCommand
        {
            Mode = CliCaptureMode.Full,
            Path = _tempDir,
            Format = ExportFormat.WebP,
        });
        Assert.True(result.Success);
        Assert.EndsWith(".webp", result.SavedPath);
    }

    // ---------- clipboard ----------

    [Fact]
    public async Task Full_WithClipboard_SetsSnapshotBitmap()
    {
        var executor = CreateExecutor();
        var result = await executor.ExecuteAsync(new CliCommand { Mode = CliCaptureMode.Full, Clipboard = true });

        Assert.True(result.Success, result.Message);
        Assert.True(result.CopiedToClipboard);
        Assert.Null(result.SavedPath);
        Assert.Contains("Copied to clipboard", result.Message);
        Assert.NotNull(_clipboard.TryGetImage());
        Assert.Equal(FakeCaptureService.BitmapWidth, _clipboard.TryGetImage()!.Width);
    }

    [Fact]
    public async Task Full_WithPathAndClipboard_BothApplied()
    {
        var executor = CreateExecutor();
        var result = await executor.ExecuteAsync(new CliCommand
        {
            Mode = CliCaptureMode.Full,
            Path = _tempDir,
            Clipboard = true,
        });

        Assert.True(result.Success);
        Assert.NotNull(result.SavedPath);
        Assert.True(result.CopiedToClipboard);
        Assert.Contains("; ", result.Message);
    }

    [Fact]
    public async Task ClipboardFailure_WithPath_ReportsPartialAndFails()
    {
        var executor = CreateExecutor();
        _clipboard.FailNextSetImage = true;

        var result = await executor.ExecuteAsync(new CliCommand
        {
            Mode = CliCaptureMode.Full,
            Path = _tempDir,
            Clipboard = true,
        });

        Assert.False(result.Success);
        Assert.NotNull(result.SavedPath);
        Assert.True(File.Exists(result.SavedPath));
        Assert.Contains("Failed to copy to clipboard", result.Message);
    }

    [Fact]
    public async Task ClipboardOnly_Failure_Fails()
    {
        var executor = CreateExecutor();
        _clipboard.FailNextSetImage = true;

        var result = await executor.ExecuteAsync(new CliCommand { Mode = CliCaptureMode.Full, Clipboard = true });

        Assert.False(result.Success);
        Assert.Contains("Failed to copy to clipboard", result.Message);
    }

    // ---------- region ----------

    [Fact]
    public async Task Region_WithGeometry_CapturesExactRect()
    {
        var executor = CreateExecutor();
        var rect = new PixelRect(10, 20, 300, 200);

        var result = await executor.ExecuteAsync(new CliCommand
        {
            Mode = CliCaptureMode.Region,
            Geometry = rect,
            Clipboard = true,
        });

        Assert.True(result.Success, result.Message);
        Assert.Equal(rect, _capture.LastRegion);
        Assert.Equal(CaptureSource.Region, _capture.LastSource);
    }

    [Fact]
    public async Task Region_WithoutGeometry_DefensiveFailure()
    {
        var executor = CreateExecutor();
        var result = await executor.ExecuteAsync(new CliCommand { Mode = CliCaptureMode.Region });

        Assert.False(result.Success);
        Assert.Contains("--geometry", result.Message);
        Assert.Null(_capture.LastSource);
    }

    [Fact]
    public async Task Window_DefensiveFailure()
    {
        var executor = CreateExecutor();
        var result = await executor.ExecuteAsync(new CliCommand { Mode = CliCaptureMode.Window });

        Assert.False(result.Success);
        Assert.Contains("GUI", result.Message);
        Assert.Null(_capture.LastSource);
    }

    [Fact]
    public async Task Gui_DefensiveFailure()
    {
        var executor = CreateExecutor();
        var result = await executor.ExecuteAsync(new CliCommand { Mode = CliCaptureMode.Gui });

        Assert.False(result.Success);
        Assert.Null(_capture.LastSource);
    }

    // ---------- 延时 / 取消 ----------

    [Fact]
    public async Task Delay_InvokedBeforeCapture_WithClampedSeconds()
    {
        var delays = new List<int>();
        var executor = CreateExecutor(delay: (s, _) => { delays.Add(s); return Task.CompletedTask; });

        var result = await executor.ExecuteAsync(new CliCommand
        {
            Mode = CliCaptureMode.Full,
            Clipboard = true,
            DelaySeconds = 7,
        });

        Assert.True(result.Success);
        Assert.Equal([7], delays);
        Assert.NotNull(_capture.LastSource); // 延时之后才抓
    }

    [Fact]
    public async Task CancelledDuringDelay_FailureNotCapture()
    {
        using var cts = new CancellationTokenSource();
        var executor = CreateExecutor(delay: (_, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });

        var result = await executor.ExecuteAsync(new CliCommand
        {
            Mode = CliCaptureMode.Full,
            Clipboard = true,
            DelaySeconds = 5,
        }, cts.Token);

        Assert.False(result.Success);
        Assert.Contains("Cancelled", result.Message);
        Assert.Null(_capture.LastSource);
    }

    [Fact]
    public async Task CancelledBeforeCapture_FailureNotCapture()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync(new CliCommand { Mode = CliCaptureMode.Full, Clipboard = true }, cts.Token);

        Assert.False(result.Success);
        Assert.Null(_capture.LastSource);
    }

    // ---------- 失败路径 ----------

    [Fact]
    public async Task CaptureThrows_FailureWithMessage()
    {
        var executor = CreateExecutor();
        _capture.ThrowOnCapture = new InvalidOperationException("screen permission denied");

        var result = await executor.ExecuteAsync(new CliCommand { Mode = CliCaptureMode.Full, Clipboard = true });

        Assert.False(result.Success);
        Assert.Contains("Capture failed", result.Message);
        Assert.Contains("screen permission denied", result.Message);
    }

    [Fact]
    public async Task SaveFailure_FailsWithReason()
    {
        var executor = CreateExecutor();
        // blocker 是普通文件：Directory.CreateDirectory 直接 IOException，导出失败但不崩
        Directory.CreateDirectory(_tempDir);
        var blocker = Path.Combine(_tempDir, "blocker");
        await File.WriteAllTextAsync(blocker, "not a directory");

        var result = await executor.ExecuteAsync(new CliCommand
        {
            Mode = CliCaptureMode.Full,
            Path = blocker,
        });

        Assert.False(result.Success);
        Assert.Contains("failed", result.Message);
    }

    // ---------- Fake 服务 ----------

    internal sealed class FakeCaptureService : ICaptureService
    {
        public const int BitmapWidth = 8;
        public const int BitmapHeight = 6;

        public Exception? ThrowOnCapture { get; set; }
        public PixelRect? LastRegion { get; private set; }
        public CaptureSource? LastSource { get; private set; }
        private readonly List<CapturedImage> _shots = [];

        public IReadOnlyList<DisplayInfo> EnumerateDisplays() =>
        [
            new DisplayInfo(0, "FakeDisplay", new PixelRect(0, 0, 1920, 1080),
                new PixelRect(0, 0, 1920, 1040), 1.0, true),
        ];

        public CapturedImage CaptureFullscreen()
        {
            LastSource = CaptureSource.Fullscreen;
            return NewShot(CaptureSource.Fullscreen);
        }

        public CapturedImage CaptureRegion(PixelRect region)
        {
            LastRegion = region;
            LastSource = CaptureSource.Region;
            return NewShot(CaptureSource.Region);
        }

        public CapturedImage CaptureDisplay(int displayIndex) => throw new NotSupportedException();

        public CapturedImage CaptureWindow(nint hwnd) => throw new NotSupportedException();

        private CapturedImage NewShot(CaptureSource source)
        {
            if (ThrowOnCapture is { } ex) throw ex;
            var bmp = new SKBitmap(new SKImageInfo(BitmapWidth, BitmapHeight, SKColorType.Bgra8888, SKAlphaType.Opaque));
            bmp.Erase(SKColors.DimGray);
            var shot = new CapturedImage(bmp, new PixelRect(0, 0, BitmapWidth, BitmapHeight), 1.0, source);
            _shots.Add(shot);
            return shot;
        }

        public void DisposeShots() { foreach (var s in _shots) s.Dispose(); _shots.Clear(); }
    }
}

/// <summary>失败注入开关的 Fake 剪贴板（基于 Core 自带的 FakeClipboardService）。</summary>
public sealed class FailInjectableClipboardService : IClipboardService
{
    public bool FailNextSetImage { get; set; }
    private readonly FakeClipboardService _inner = new();

    public bool TrySetImage(SKBitmap bitmap)
    {
        if (FailNextSetImage) return false;
        return _inner.TrySetImage(bitmap);
    }

    public SKBitmap? TryGetImage() => _inner.TryGetImage();
    public bool TrySetText(string text) => _inner.TrySetText(text);
    public string? TryGetImageFilePath() => _inner.TryGetImageFilePath();
}
