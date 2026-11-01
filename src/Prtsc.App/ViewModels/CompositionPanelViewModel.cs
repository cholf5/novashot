using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Prtsc.Core.Annotation;
using Prtsc.Core.Editor;
using Prtsc.Core.Utils;
using SkiaSharp;

namespace Prtsc.App.ViewModels;

/// <summary>
/// 合成外观面板 VM（M6b）：背景（无/纯色/24 渐变）、Padding、圆角、阴影、内衬、边框。
/// 每个属性读 <see cref="EditorViewModel.CompositionState"/>，写回时用 <c>with</c> 重建
/// ——与 <see cref="StylePanelViewModel"/> 同一模式；改动不进 Undo。
/// </summary>
public sealed class CompositionPanelViewModel : ObservableObject
{
    public EditorViewModel Editor { get; }

    /// <summary>与 StylePanel 共用的 8 色板。</summary>
    public IReadOnlyList<SKColor> PresetColors => StylePanelViewModel.PresetColorsList;

    public IReadOnlyList<GradientPresets.Preset> Gradients => GradientPresets.All;

    public CompositionPanelViewModel(EditorViewModel editor)
    {
        Editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Editor.PropertyChanged += OnEditorPropertyChanged;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.CompositionState))
            OnStateChanged();
    }

    /// <summary>CompositionState 被整体替换时刷新全部派生属性。</summary>
    private void OnStateChanged()
    {
        OnPropertyChanged(string.Empty);
    }

    private EditorState State => Editor.CompositionState;

    private void Apply(Func<EditorState, EditorState> mutate)
    {
        Editor.CompositionState = mutate(State).Clamped();
    }

    // ---- 背景 ----

    public bool IsBgNone
    {
        get => State.Background == BackgroundKind.None;
        set { if (value) Apply(s => s with { Background = BackgroundKind.None }); }
    }

    public bool IsBgSolid
    {
        get => State.Background == BackgroundKind.SolidColor;
        set { if (value) Apply(s => s with { Background = BackgroundKind.SolidColor }); }
    }

    public bool IsBgGradient
    {
        get => State.Background == BackgroundKind.Gradient;
        set { if (value) Apply(s => s with { Background = BackgroundKind.Gradient }); }
    }

    /// <summary>色板点击（code-behind 调用）：设纯色背景。</summary>
    public void SetSolidColor(SKColor color)
        => Apply(s => s with { Background = BackgroundKind.SolidColor, BackgroundColor = color });

    /// <summary>渐变格子点击（code-behind 调用）。</summary>
    public void SetGradient(string css)
        => Apply(s => s with { Background = BackgroundKind.Gradient, BackgroundGradient = css });

    // ---- 几何 ----

    public double Padding
    {
        get => State.Padding;
        set => Apply(s => s with { Padding = (int)Math.Round(value) });
    }

    public double CornerRadius
    {
        get => State.CornerRadius;
        set => Apply(s => s with { CornerRadius = (float)value });
    }

    // ---- 阴影 ----

    public bool ShadowOn
    {
        get => State.ShadowSize > 0f;
        set => Apply(s => s with { ShadowSize = value ? (s.ShadowSize > 0 ? s.ShadowSize : 12f) : 0f });
    }

    public double ShadowSize
    {
        get => State.ShadowSize > 0 ? State.ShadowSize : 12d; // 关闭时给滑杆一个默认展示值
        set { if (State.ShadowSize > 0) Apply(s => s with { ShadowSize = (float)value }); }
    }

    // ---- 边框 ----

    public bool BorderOn
    {
        get => State.BorderWidth > 0f;
        set => Apply(s => s with { BorderWidth = value ? (s.BorderWidth > 0 ? s.BorderWidth : 4f) : 0f });
    }

    public double BorderWidth
    {
        get => State.BorderWidth > 0 ? State.BorderWidth : 4d;
        set { if (State.BorderWidth > 0) Apply(s => s with { BorderWidth = (float)value }); }
    }

    /// <summary>ComboBox SelectedIndex：0=Outside 1=Center 2=Inside。</summary>
    public int BorderStyleIndex
    {
        get => (int)State.Border;
        set => Apply(s => s with { Border = (BorderStyle)Math.Clamp(value, 0, 2) });
    }

    public void SetBorderColor(SKColor color) => Apply(s => s with { BorderColor = color });

    // ---- 内衬 ----

    public bool InsetOn
    {
        get => State.InsetSize > 0f;
        set => Apply(s => s with { InsetSize = value ? (s.InsetSize > 0 ? s.InsetSize : 6f) : 0f });
    }

    public double InsetSize
    {
        get => State.InsetSize > 0 ? State.InsetSize : 6d;
        set { if (State.InsetSize > 0) Apply(s => s with { InsetSize = (float)value }); }
    }

    public void SetInsetColor(SKColor color) => Apply(s => s with { InsetColor = color });

    public void Dispose()
    {
        Editor.PropertyChanged -= OnEditorPropertyChanged;
    }
}
