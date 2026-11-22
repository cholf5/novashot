using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Novashot.Core.Common;

// Avalonia 也有 PixelRect：本文件一律用 Core 的物理像素矩形（与 OverlayWindow 同别名口径）
using PixelRect = Novashot.Core.Common.PixelRect;

namespace Novashot.App.Controls;

/// <summary>
/// B-3 M2-3b：跨屏选框会话协调器——一次 Region 捕获会话内的唯一权威选框状态。
///
/// - 权威选框存放在「活动帧」＝最近一次按下所在屏的 DIP 坐标系；拖拽数学全部复用
///   <see cref="SelectionGeometry"/>，单屏（N=1）时与旧 SelectionAdorner 本地行为逐位等价；
/// - 输入按「上报屏」（捕获窗口所在屏）换算物理像素、再映射进活动帧：指针捕获期间
///   OS 把整条拖拽事件送给按下时的窗口，指针越到另一屏后收到的是越界本地坐标
///   （线性换算不依赖界内），跨屏续拖因此成立；
/// - 渲染经 <see cref="SelectionInFrame"/> 按目标屏各自 ScaleFactor 映射，矩形可越出
///   目标屏（窗口自然裁剪，暗化/描边/手柄即得跨屏视觉）；
/// - 钳制边界＝虚拟桌面 union（<see cref="UnionInFrame"/>），不再是单屏 bounds；
/// - <see cref="ConfirmRegion"/> 输出物理像素 region，交给 ICaptureService.CaptureRegion
///   （Mac/Linux/WGC/GDI 均已支持跨屏逐屏拼接合成）。
///
/// 纯几何 + 状态机，无窗口依赖；由 MainWindow 按 EnumerateDisplays 快照构造，
/// 注入该会话的每个 OverlayWindow（各自订阅 <see cref="Changed"/> 广播）。
/// </summary>
public sealed class SelectionSession
{
    private enum DragMode { None, Creating, Moving, Resizing }

    private readonly IReadOnlyList<DisplayInfo> _displays;

    private DragMode _mode = DragMode.None;
    private SelectionGeometry.Grip _activeGrip = SelectionGeometry.Grip.Outside;
    private Point _dragStart;          // 活动帧 DIP
    private Rect _dragStartRect;       // 活动帧 DIP
    private Rect? _selection;          // 权威选框，活动帧 DIP
    private int _framePos;             // 活动帧 = _displays[_framePos]

    public SelectionSession(IReadOnlyList<DisplayInfo> displays)
    {
        _displays = displays.Count > 0
            ? displays
            : throw new ArgumentException("SelectionSession needs at least one display", nameof(displays));
    }

    /// <summary>指针拖拽进行中（Press 与 Release 之间）。</summary>
    public bool HasActiveDrag => _mode != DragMode.None;

    /// <summary>最近一次按下的 grip（供光标反馈；Creating 语义恒为 SE，对齐旧实现）。</summary>
    public SelectionGeometry.Grip ActiveGrip => _activeGrip;

    /// <summary>活动帧所在屏的 Index（坐标权威帧；键盘微调/确认换算都以它为基准）。</summary>
    public int ActiveDisplayIndex => _displays[_framePos].Index;

    /// <summary>
    /// 工具环应显示在哪块屏：选框物理中心所在的屏（跨屏拖动时环跟着选框走）；
    /// 中心落在屏间隙（X11 可配出）时回退活动屏。
    /// </summary>
    public int RingDisplayIndex
    {
        get
        {
            if (_selection is not { } sel) return ActiveDisplayIndex;
            var phys = DipToPhysical(_displays[_framePos], sel);
            var cx = phys.X + phys.Width / 2;
            var cy = phys.Y + phys.Height / 2;
            foreach (var d in _displays)
            {
                if (d.Bounds.Contains(cx, cy)) return d.Index;
            }
            return ActiveDisplayIndex;
        }
    }

    /// <summary>权威选框变化广播（各 OverlayWindow 据此把映射后的矩形写回自己的 adorner）。</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 指针按下。grip 判定基于既有选框映射到「本屏帧」后的命中测试——在另一屏上按住
    /// 跨屏选框的切片/手柄时，活动帧切换到该屏（_selection 随之重表达，物理语义不变）。
    /// </summary>
    public void Press(int displayIndex, Point localDip, KeyModifiers mods)
    {
        if (_mode != DragMode.None) return; // 拖拽中的指针不重复受理
        var pos = PosOfIndex(displayIndex);
        if (pos < 0) return;

        // 帧切换前先把旧帧权威选框换算进新帧（_selection 表达在旧 _framePos 里，
        // 顺序反了会把旧帧数值误当新帧读——B-3 测试实证过该陷阱）
        var selLocal = _selection is { } cur
            ? PhysicalToLocalDipRect(_displays[pos], DipToPhysical(_displays[_framePos], cur))
            : (Rect?)null;

        _framePos = pos;
        var grip = SelectionGeometry.HitTest(selLocal, localDip,
            SelectionGeometry.GripHitHalfPad, SelectionGeometry.EdgeHitSlop);

        // 锚点与后续拖拽点同网格（B-3 review Minor-3）：Drag 每点先量化到上报屏物理网格，
        // 锚点存原始 DIP 的话缩放屏上首段 dx 含 ≤1 物理像素网格差（亚像素偏移无累积但不同源）
        var anchor = QuantizedLocalDip(_displays[pos], localDip);

        if (grip == SelectionGeometry.Grip.Outside || selLocal is null)
        {
            _mode = DragMode.Creating;
            _activeGrip = SelectionGeometry.Grip.SE;
            _dragStart = anchor;
            _dragStartRect = new Rect(anchor, new Size(0, 0));
            _selection = _dragStartRect;
        }
        else
        {
            _mode = grip == SelectionGeometry.Grip.Inside ? DragMode.Moving : DragMode.Resizing;
            _activeGrip = grip;
            _dragStart = anchor;
            _dragStartRect = selLocal.Value;
            _selection = selLocal.Value;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 拖拽移动。<paramref name="displayIndex"/> 是上报窗口所在屏：指针被捕获期间该窗口
    /// 可能已经指到另一屏（坐标越界），换算公式对界外坐标同样成立。
    /// </summary>
    public void Drag(int displayIndex, Point localDip, KeyModifiers mods)
    {
        if (_mode == DragMode.None) return;
        var rpos = PosOfIndex(displayIndex);
        if (rpos < 0) return;

        var phys = LocalToPhysical(_displays[rpos], localDip);
        var frame = _displays[_framePos];
        var p = PhysicalToLocalDip(frame, phys);

        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        var bounds = UnionInFrame(frame);

        if (_activeGrip == SelectionGeometry.Grip.Inside)
        {
            // 平移直接 clamp 后返回，不参与 Ctrl/Shift（对齐旧实现）
            var nx = Math.Clamp(_dragStartRect.X + dx, bounds.X, Math.Max(bounds.X, bounds.Right - _dragStartRect.Width));
            var ny = Math.Clamp(_dragStartRect.Y + dy, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - _dragStartRect.Height));
            _selection = new Rect(nx, ny, _dragStartRect.Width, _dragStartRect.Height);
        }
        else
        {
            var r = SelectionGeometry.ApplyDrag(_dragStartRect, dx, dy, _activeGrip);
            if (mods.HasFlag(KeyModifiers.Control))
                r = SelectionGeometry.ApplyAspectLock(r, _dragStartRect, _activeGrip);
            if (mods.HasFlag(KeyModifiers.Shift))
                r = SelectionGeometry.ApplyCenterSymmetric(r, _dragStartRect, _activeGrip);
            _selection = SelectionGeometry.ClampAndNormalizeTo(r, bounds);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>释放。太小的框视为无效并清空（对齐旧实现的 MinSize 语义）。</summary>
    public void Release(int displayIndex)
    {
        if (_mode == DragMode.None) return;
        _mode = DragMode.None;
        if (_selection is { } s && (s.Width < SelectionGeometry.MinSize || s.Height < SelectionGeometry.MinSize))
            _selection = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 键盘微调（←/→/↑/↓ ±1，Shift ±10，Ctrl 改右下角=缩放），钳到虚拟桌面 union。
    /// 返回是否消费了该键（非方向键 false，调用方不必 Handled）。
    /// </summary>
    public bool Nudge(Key key, bool ctrl, bool shift)
    {
        if (_selection is not { } sel) return false;
        var step = shift ? 10.0 : 1.0;
        double dx = 0, dy = 0;
        switch (key)
        {
            case Key.Left: dx = -step; break;
            case Key.Right: dx = step; break;
            case Key.Up: dy = -step; break;
            case Key.Down: dy = step; break;
            default: return false;
        }

        var bounds = UnionInFrame(_displays[_framePos]);
        if (ctrl)
        {
            var nw = Math.Clamp(sel.Width + dx, SelectionGeometry.MinSize, Math.Max(SelectionGeometry.MinSize, bounds.Right - sel.X));
            var nh = Math.Clamp(sel.Height + dy, SelectionGeometry.MinSize, Math.Max(SelectionGeometry.MinSize, bounds.Bottom - sel.Y));
            _selection = new Rect(sel.X, sel.Y, nw, nh);
        }
        else
        {
            var nx = Math.Clamp(sel.X + dx, bounds.X, Math.Max(bounds.X, bounds.Right - sel.Width));
            var ny = Math.Clamp(sel.Y + dy, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - sel.Height));
            _selection = new Rect(nx, ny, sel.Width, sel.Height);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>清空选框（Esc 清除语义；广播后所有 overlay 的选框一并消失）。</summary>
    public void Clear()
    {
        _selection = null;
        _mode = DragMode.None;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>权威选框映射到指定屏的本地 DIP 帧（可越界；无选框返回 null）。</summary>
    public Rect? SelectionInFrame(int displayIndex)
    {
        if (_selection is not { } sel) return null;
        var pos = PosOfIndex(displayIndex);
        if (pos < 0) return null;
        var phys = DipToPhysical(_displays[_framePos], sel);
        return PhysicalToLocalDipRect(_displays[pos], phys);
    }

    /// <summary>
    /// 确认：输出物理像素 region（虚拟桌面坐标）。无选框或尺寸不足时返回 null，
    /// 调用方回退自身整屏语义（对齐旧 TryConfirmSelection）。
    /// </summary>
    public PixelRect? ConfirmRegion()
    {
        if (_selection is not { } sel) return null;
        if (sel.Width < 1 || sel.Height < 1) return null;
        var phys = DipToPhysical(_displays[_framePos], sel);
        return phys.IsEmpty ? null : phys;
    }

    // ------------------------------------------------------------------------
    //  internals
    // ------------------------------------------------------------------------
    private int PosOfIndex(int displayIndex)
    {
        for (var i = 0; i < _displays.Count; i++)
        {
            if (_displays[i].Index == displayIndex) return i;
        }
        return -1;
    }

    private static PixelPoint LocalToPhysical(DisplayInfo d, Point dip)
    {
        var s = d.ScaleFactor;
        return new PixelPoint(
            (int)Math.Round(d.Bounds.X + dip.X * s),
            (int)Math.Round(d.Bounds.Y + dip.Y * s));
    }

    private static Point PhysicalToLocalDip(DisplayInfo d, PixelPoint phys)
    {
        var s = d.ScaleFactor;
        return new Point((phys.X - d.Bounds.X) / s, (phys.Y - d.Bounds.Y) / s);
    }

    /// <summary>本地 DIP 点量化到本屏物理网格再回 DIP（与 Drag 路径同口径，见 Press 内注释）。</summary>
    private static Point QuantizedLocalDip(DisplayInfo d, Point dip) =>
        PhysicalToLocalDip(d, LocalToPhysical(d, dip));

    private static Rect PhysicalToLocalDipRect(DisplayInfo d, PixelRect p)
    {
        var s = d.ScaleFactor;
        return new Rect((p.X - d.Bounds.X) / s, (p.Y - d.Bounds.Y) / s, p.Width / s, p.Height / s);
    }

    /// <summary>DIP 矩形 → 物理像素（Math.Round 舍入、宽高至少 1，与旧 OverlayWindow.DipToPhysical 同口径）。</summary>
    private static PixelRect DipToPhysical(DisplayInfo d, Rect dip)
    {
        var s = d.ScaleFactor;
        var x = (int)Math.Round(d.Bounds.X + dip.X * s);
        var y = (int)Math.Round(d.Bounds.Y + dip.Y * s);
        var w = (int)Math.Round(dip.Width * s);
        var h = (int)Math.Round(dip.Height * s);
        if (w < 1) w = 1;
        if (h < 1) h = 1;
        return new PixelRect(x, y, w, h);
    }

    /// <summary>虚拟桌面 union（全部显示器 Bounds 联集）映射进活动帧，原点可为负。</summary>
    private Rect UnionInFrame(DisplayInfo frame)
    {
        var minX = _displays.Min(d => d.Bounds.X);
        var minY = _displays.Min(d => d.Bounds.Y);
        var maxX = _displays.Max(d => d.Bounds.Right);
        var maxY = _displays.Max(d => d.Bounds.Bottom);
        var s = frame.ScaleFactor;
        return new Rect(
            (minX - frame.Bounds.X) / s,
            (minY - frame.Bounds.Y) / s,
            (maxX - minX) / s,
            (maxY - minY) / s);
    }
}
