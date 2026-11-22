using Microsoft.Extensions.Logging.Abstractions;
using Novashot.Platform.Mac.Capture;
using Novashot.Platform.Mac.Interop;
using SkiaSharp;

namespace Novashot.Mac.Tests;

/// <summary>
/// review I-2 回归：L-1 起 ICaptureService 会跑在非 UI 线程（CLI 转发 headless 在管道连接 Task 上执行），
/// 与 UI 线程热键抓帧并发。旧实现的实例字段 List&lt;uint&gt; _rawIds（EnumerateDisplays Clear/Add、
/// 捕获时跨方法读）在并发下互相覆写 → 张冠李戴 / IndexOutOfRange；快照语义（DisplayInfo 与
/// displayId 局部配对、共享字段已删除）结构性消除竞态。
///
/// 测得到什么程度（如实）：旧实现的竞态窗口在服务内部「枚举返回 ↔ id 读取」两行之间，无外部 seam，
/// 无法决定性复现单次错配——本测试以 8 线程 × 12 轮 hammer 轮转显示器组，用
/// <c>CapturedImage.Bounds 与位图实际尺寸逐像素一致</c> 断言抓粗回归（错配 id / 集合改动后
/// Collection-modified / IndexOutOfRange）；新实现确定性通过、无 flake。错配的决定性回归由
/// 「共享字段已删除」的结构性审查兜底（同 D-1 review pass 的「竞态逻辑整体下沉、无可测残留」判例）。
/// </summary>
public class MacCaptureServiceConcurrencyTests
{
    /// <summary>三组互不重叠的显示器：尺寸/id 各不相同 → 错配必可观测。</summary>
    private static readonly (uint Id, int W, int H)[] SetA = [(101, 40, 30)];
    private static readonly (uint Id, int W, int H)[] SetB = [(201, 80, 60)];
    private static readonly (uint Id, int W, int H)[] SetC = [(301, 120, 90)];

    private sealed class RotatingDisplayApi : IMacDisplayApi
    {
        private int _counter;
        public long EnumerateCount;

        public MacDisplayRaw[] EnumerateDisplays()
        {
            Interlocked.Increment(ref EnumerateCount);
            var set = (Interlocked.Increment(ref _counter) % 3) switch
            {
                0 => SetA,
                1 => SetB,
                _ => SetC,
            };
            return [.. set.Select(d => new MacDisplayRaw(d.Id, (uint)d.W, (uint)d.H, new CGRect(0, 0, d.W, d.H), IsMain: true))];
        }

        public nint CreateDisplayImage(uint displayId)
        {
            var spec = SetA.Concat(SetB).Concat(SetC).First(d => d.Id == displayId);
            return FakeDisplayApi.MakeSolidImage(spec.W, spec.H, SKColors.DimGray);
        }

        public nint CreateWindowImage(CGRect screenBounds, uint windowId) => nint.Zero;

        public bool TryGetWindowBounds(uint windowId, out CGRect bounds)
        {
            bounds = default;
            return false;
        }
    }

    [Fact]
    public async Task ConcurrentCaptures_WithRotatingDisplaySets_BoundsAlwaysMatchBitmap()
    {
        var api = new RotatingDisplayApi();
        var svc = new MacCaptureService(api, NullLogger<MacCaptureService>.Instance);
        var validSizes = new[] { (40, 30), (80, 60), (120, 90) };
        var failures = new List<string>();

        var threads = 8;
        var rounds = 12;
        var barrier = new Barrier(threads);
        var tasks = new List<Task>();
        for (var t = 0; t < threads; t++)
        {
            tasks.Add(Task.Run(() =>
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                for (var r = 0; r < rounds; r++)
                {
                    using var shot = svc.CaptureFullscreen();
                    var w = shot.Width;
                    var h = shot.Height;
                    // 1) 抓到的必须是这三组之一（没有未知 id / 空图）
                    if (!validSizes.Contains((w, h)))
                        lock (failures) failures.Add($"Unexpected capture size {w}x{h}");
                    // 2) Bounds（来自本线程枚举的那组）与位图实际尺寸必须一致——
                    //    旧实现 id 张冠李戴时即错配（枚举 A 组、图像取 B 组）
                    if (shot.Bounds.Width != w || shot.Bounds.Height != h)
                        lock (failures) failures.Add(
                            $"Bounds {shot.Bounds} vs bitmap {w}x{h} — id/display pairing broken");
                }
            }));
        }

        // 枚举路径并发插入（Region 流程逐屏 CaptureDisplay 也会触发）
        var enumerator = Task.Run(() =>
        {
            for (var i = 0; i < rounds; i++)
                _ = svc.EnumerateDisplays();
        });

        await Task.WhenAll(tasks);
        await enumerator;

        Assert.True(api.EnumerateCount >= threads * rounds, "fake should have been hammered");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(10)));
    }
}
