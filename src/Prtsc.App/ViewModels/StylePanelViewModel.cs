using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Prtsc.App.Localization;
using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using SkiaSharp;

namespace Prtsc.App.ViewModels;

/// <summary>
/// 右侧属性面板的视图模型：包装 <see cref="EditorViewModel"/>，把当前选中标注的样式
/// （颜色 / 描边宽 / 字号）映射成可绑定的属性；无选中时直接编辑 <see cref="EditorViewModel.CurrentStyle"/>。
///
/// 样式改动**不进 Undo/Redo**（参考 Flameshot）。原地修改后通过
/// <see cref="AnnotationDocument.NotifyMutated"/> 触发画布重绘。
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

    public StylePanelViewModel(EditorViewModel editor)
    {
        Editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Editor.PropertyChanged += OnEditorPropertyChanged;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.Selection)
            or nameof(EditorViewModel.CurrentStyle))
        {
            // 选中变化会影响所有派生属性：颜色/宽/字号可用性/标签
            OnPropertyChanged(nameof(Stroke));
            OnPropertyChanged(nameof(StrokeHex));
            OnPropertyChanged(nameof(StrokeWidth));
            OnPropertyChanged(nameof(FontSize));
            OnPropertyChanged(nameof(IsFontSizeAvailable));
            OnPropertyChanged(nameof(SelectionKindLabel));
        }
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
        => Editor.Selection is TextAnnotation || Editor.CurrentTool == Prtsc.Core.Annotation.Tools.ToolKind.Text;

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

    public string SelectionKindLabel => Editor.Selection switch
    {
        null                    => Loc.T("Kind.None"),
        RectangleAnnotation _   => Loc.T("Kind.Rectangle"),
        EllipseAnnotation _     => Loc.T("Kind.Ellipse"),
        ArrowCurvedAnnotation _ => Loc.T("Kind.ArrowCurved"),
        ArrowStraightAnnotation _ => Loc.T("Kind.ArrowStraight"),
        LineAnnotation _        => Loc.T("Kind.Line"),
        TextAnnotation _        => Loc.T("Kind.Text"),
        NumberAnnotation _      => Loc.T("Kind.Number"),
        SpotlightAnnotation _   => Loc.T("Kind.Spotlight"),
        PencilAnnotation _      => Loc.T("Kind.Pencil"),
        HighlighterAnnotation _ => Loc.T("Kind.Highlighter"),
        MosaicAnnotation _      => Loc.T("Kind.Mosaic"),
        BlurAnnotation _        => Loc.T("Kind.Blur"),
        InvertAnnotation _      => Loc.T("Kind.Invert"),
        _                       => Editor.Selection!.GetType().Name,
    };

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
    }
}
