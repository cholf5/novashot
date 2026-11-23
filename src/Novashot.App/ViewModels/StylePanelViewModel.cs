using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Novashot.App.Localization;
using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Editing;
using Novashot.Core.Annotation.History;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Editor;
using SkiaSharp;

namespace Novashot.App.ViewModels;

/// <summary>
/// 右侧属性面板的视图模型：包装 <see cref="EditorViewModel"/>，把当前选中标注的样式
/// （颜色 / 描边宽 / 字号）映射成可绑定的属性；无选中时直接编辑 <see cref="EditorViewModel.CurrentStyle"/>。
///
/// 样式改动**不进 Undo/Redo**（参考 Flameshot）。原地修改后通过
/// <see cref="AnnotationDocument.NotifyMutated"/> 触发画布重绘。
///
/// E-1 M9：持有会话级 <see cref="ColorHistory"/>（overlay 取色写入），
/// 面板在预设 8 色旁显示历史行；点历史色 = <see cref="Stroke"/> 既有设置路径。
/// </summary>
public sealed partial class StylePanelViewModel : ObservableObject, IDisposable
{
    /// <summary>面板预设颜色（Flameshot 风格 8 色）。</summary>
    public static readonly IReadOnlyList<SKColor> PresetColorsList = new[]
    {
        new SKColor(0xE5, 0x3E, 0x3E), // red
        new SKColor(0xF5, 0x9E, 0x0B), // orange
        new SKColor(0xFC, 0xD3, 0x4D), // yellow
        new SKColor(0x10, 0xB9, 0x81), // green
        new SKColor(0x3B, 0x82, 0xF6), // blue
        new SKColor(0x8B, 0x5C, 0xF6), // purple
        new SKColor(0xF3, 0xF4, 0xF6), // light
        new SKColor(0x1F, 0x29, 0x37), // near-black
    };

    public IReadOnlyList<SKColor> PresetColors => PresetColorsList;

    public EditorViewModel Editor { get; }

    private readonly ColorHistory? _colorHistory;

    public StylePanelViewModel(EditorViewModel editor, ColorHistory? colorHistory = null)
    {
        Editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _colorHistory = colorHistory;
        if (_colorHistory is not null)
            _colorHistory.Changed += OnColorHistoryChanged;
        Editor.PropertyChanged += OnEditorPropertyChanged;
        Editor.Document.Changed += OnDocumentChanged;
    }

    /// <summary>E-1 M9：会话级取色历史（最新在前；无历史实例时空表）。</summary>
    public IReadOnlyList<SKColor> HistoryColors => _colorHistory?.Colors ?? Array.Empty<SKColor>();

    /// <summary>历史行显隐（空则整行隐藏）。</summary>
    public bool HasColorHistory => HistoryColors.Count > 0;

    private void OnColorHistoryChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HistoryColors));
        OnPropertyChanged(nameof(HasColorHistory));
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.Selection)
            or nameof(EditorViewModel.CurrentTool)
            or nameof(EditorViewModel.CurrentStyle))
        {
            // 选中变化会影响所有派生属性：颜色/宽/字号可用性/标签
            OnPropertyChanged(nameof(Stroke));
            OnPropertyChanged(nameof(StrokeHex));
            OnPropertyChanged(nameof(StrokeWidth));
            OnPropertyChanged(nameof(FontSize));
            OnPropertyChanged(nameof(IsFontSizeAvailable));
            OnPropertyChanged(nameof(SelectionKindLabel));
            RefreshInspector();
        }
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => RefreshInspector();

    /// <summary>几何/效果/双态组的状态刷新（选中变化与文档级变化共用）。</summary>
    private void RefreshInspector()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsGeometryVisible));
        OnPropertyChanged(nameof(IsSizeEditable));
        OnPropertyChanged(nameof(IsEffectVisible));
        OnPropertyChanged(nameof(EffectLabel));
        OnPropertyChanged(nameof(EffectMin));
        OnPropertyChanged(nameof(EffectMax));
        OnPropertyChanged(nameof(SelX));
        OnPropertyChanged(nameof(SelY));
        OnPropertyChanged(nameof(SelW));
        OnPropertyChanged(nameof(SelH));
        OnPropertyChanged(nameof(EffectStrength));
    }

    /// <summary>当前生效颜色：优先取选中标注的 Style，否则取 CurrentStyle。</summary>
    public SKColor Stroke
    {
        get => Editor.Selection?.Style.Stroke ?? Editor.CurrentStyle.Stroke;
        set
        {
            if (value == Stroke) return;
            ApplyStyleChange(style => style with { Stroke = value });
            OnPropertyChanged();
            OnPropertyChanged(nameof(StrokeHex));
        }
    }

    /// <summary>#RRGGBB（面板上给用户看的色值；SKColor.ToString 是 #AARRGGBB，太长）。</summary>
    public string StrokeHex => $"#{Stroke.Red:X2}{Stroke.Green:X2}{Stroke.Blue:X2}";

    public float StrokeWidth
    {
        get => Editor.Selection?.Style.StrokeWidth ?? Editor.CurrentStyle.StrokeWidth;
        set
        {
            var v = Math.Clamp(value, 1f, 32f);
            if (Math.Abs(v - StrokeWidth) < 0.001f) return;
            ApplyStyleChange(style => style with { StrokeWidth = v });
            OnPropertyChanged();
        }
    }

    public bool IsFontSizeAvailable
        => Editor.Selection is TextAnnotation || Editor.CurrentTool == Novashot.Core.Annotation.Tools.ToolKind.Text;

    /// <summary>字号：仅 <see cref="TextAnnotation"/> 选中时有意义；未选中时读默认 24。</summary>
    public float FontSize
    {
        get => (Editor.Selection as TextAnnotation)?.FontSize ?? 24f;
        set
        {
            var v = Math.Clamp(value, 8f, 128f);
            if (Editor.Selection is TextAnnotation text)
            {
                if (Math.Abs(text.FontSize - v) < 0.001f) return;
                text.FontSize = v;
                Editor.Document.NotifyMutated();
                OnPropertyChanged();
            }
        }
    }

    public string SelectionKindLabel => Loc.KindLabel(Editor.Selection);

    // ---- M-Inspector：几何 / 效果 / 双态 ----

    /// <summary>true = 选中态（Inspector 四组显示）；false = 默认样式态（只显示公共组）。</summary>
    public bool HasSelection => Editor.Selection is not null;

    /// <summary>几何组显隐（有选中即显示；X/Y 全类型可编辑）。</summary>
    public bool IsGeometryVisible => Editor.Selection is not null;

    /// <summary>宽/高可编辑（盒状族）；false 时对应输入框禁用（X/Y 仍可编辑）。</summary>
    public bool IsSizeEditable => Editor.Selection is { } s && InspectorGeometry.SupportsResize(s);

    /// <summary>效果组显隐（Mosaic/Blur）。</summary>
    public bool IsEffectVisible => Editor.Selection is MosaicAnnotation or BlurAnnotation;

    /// <summary>效果滑条标签：Mosaic=颗粒度、Blur=强度（语言无关引用 Loc key）。</summary>
    public string EffectLabel => Editor.Selection is MosaicAnnotation
        ? Loc.T("Panel.BlockSize")
        : Loc.T("Panel.Strength");

    /// <summary>效果滑条范围（随选中类型切换：Mosaic=BlockSize 4..48，Blur=Sigma 2..40）。</summary>
    public float EffectMin => Editor.Selection is BlurAnnotation ? BlurAnnotation.MinSigma : MosaicAnnotation.MinBlockSize;

    /// <summary>效果滑条上限。</summary>
    public float EffectMax => Editor.Selection is BlurAnnotation ? BlurAnnotation.MaxSigma : MosaicAnnotation.MaxBlockSize;

    /// <summary>选中标注逻辑几何左上角 X（GeometryRect 口径 = 手柄空间）。写入走 MoveAnnotationCommand（进 Undo，带画布 clamp）。</summary>
    public float SelX
    {
        get => Editor.Selection?.GeometryRect.Left ?? 0f;
        set
        {
            if (Editor.Selection is not { } sel) return;
            var geo = sel.GeometryRect;
            var delta = InspectorGeometry.MoveDelta(geo, value, geo.Top, Editor.Document.Width, Editor.Document.Height);
            if (delta.X != 0 || delta.Y != 0)
                Editor.History.Execute(new MoveAnnotationCommand(sel, delta, Editor.Document));
            RefreshInspector();
        }
    }

    /// <summary>选中标注逻辑几何左上角 Y。语义同 <see cref="SelX"/>。</summary>
    public float SelY
    {
        get => Editor.Selection?.GeometryRect.Top ?? 0f;
        set
        {
            if (Editor.Selection is not { } sel) return;
            var geo = sel.GeometryRect;
            var delta = InspectorGeometry.MoveDelta(geo, geo.Left, value, Editor.Document.Width, Editor.Document.Height);
            if (delta.X != 0 || delta.Y != 0)
                Editor.History.Execute(new MoveAnnotationCommand(sel, delta, Editor.Document));
            RefreshInspector();
        }
    }

    /// <summary>选中标注逻辑几何宽。写入 = SE 手柄移到目标点（进 Undo）；仅盒状族可写，其余 no-op。</summary>
    public float SelW
    {
        get => Editor.Selection?.GeometryRect.Width ?? 0f;
        set
        {
            if (Editor.Selection is not { } sel || !IsSizeEditable) return;
            var geo = sel.GeometryRect;
            var target = InspectorGeometry.ResizeTarget(geo, value, geo.Height, Editor.Document.Width, Editor.Document.Height);
            var seBefore = new SKPoint(geo.Right, geo.Bottom);
            if (target != seBefore)
                Editor.History.Execute(new ResizeAnnotationCommand(sel, HandleKind.BottomRight, seBefore, target, Editor.Document));
            RefreshInspector();
        }
    }

    /// <summary>选中标注逻辑几何高。语义同 <see cref="SelW"/>。</summary>
    public float SelH
    {
        get => Editor.Selection?.GeometryRect.Height ?? 0f;
        set
        {
            if (Editor.Selection is not { } sel || !IsSizeEditable) return;
            var geo = sel.GeometryRect;
            var target = InspectorGeometry.ResizeTarget(geo, geo.Width, value, Editor.Document.Width, Editor.Document.Height);
            var seBefore = new SKPoint(geo.Right, geo.Bottom);
            if (target != seBefore)
                Editor.History.Execute(new ResizeAnnotationCommand(sel, HandleKind.BottomRight, seBefore, target, Editor.Document));
            RefreshInspector();
        }
    }

    /// <summary>
    /// 效果强度：Mosaic 读 BlockSize、Blur 读 Sigma。就地改不进 Undo（与颜色/描边同策略），
    /// NotifyMutated 触发重绘。
    /// </summary>
    public float EffectStrength
    {
        get => Editor.Selection switch
        {
            MosaicAnnotation m => m.BlockSize,
            BlurAnnotation b   => b.Sigma,
            _                  => 0f,
        };
        set
        {
            switch (Editor.Selection)
            {
                case MosaicAnnotation m:
                    var nextBlock = (int)Math.Clamp(value, MosaicAnnotation.MinBlockSize, MosaicAnnotation.MaxBlockSize);
                    if (nextBlock != m.BlockSize)
                    {
                        m.BlockSize = nextBlock;
                        Editor.Document.NotifyMutated();
                    }
                    break;
                case BlurAnnotation b:
                    var nextSigma = Math.Clamp(value, BlurAnnotation.MinSigma, BlurAnnotation.MaxSigma);
                    if (Math.Abs(nextSigma - b.Sigma) > 0.001f)
                    {
                        b.Sigma = nextSigma;
                        Editor.Document.NotifyMutated();
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// 把 <paramref name="mutate"/> 同时应用到选中标注（若有）和 CurrentStyle——
    /// 用户改一次红→2px 后接着画的新标注也保持一致（Flameshot 行为）。
    /// </summary>
    private void ApplyStyleChange(Func<AnnotationStyle, AnnotationStyle> mutate)
    {
        var newStyle = mutate(Editor.CurrentStyle);
        Editor.CurrentStyle = newStyle;

        if (Editor.Selection is { } sel)
        {
            sel.Style = mutate(sel.Style);
            Editor.Document.NotifyMutated();
        }
    }

    public void Dispose()
    {
        Editor.PropertyChanged -= OnEditorPropertyChanged;
        Editor.Document.Changed -= OnDocumentChanged;
        if (_colorHistory is not null)
            _colorHistory.Changed -= OnColorHistoryChanged;
    }
}
