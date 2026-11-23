using CommunityToolkit.Mvvm.ComponentModel;
using Novashot.Core.Annotation.Model;

namespace Novashot.App.ViewModels;

/// <summary>
/// 左侧元素列表的单行（M-Inspector）。列表顺序 = z-order（栈顶在顶）；
/// 摘要与图标在构造时确定——文本改动等文档级变化经 <c>Document.Changed</c> 全量重建，
/// 不需要行内刷新。
/// </summary>
public sealed class ElementListItemViewModel : ObservableObject
{
    public AnnotationBase Annotation { get; }

    /// <summary>行摘要：Text→内容截 12 字、Number→#序号、其余→类型名（Loc）。</summary>
    public string Summary { get; }

    /// <summary>图标资源 key（App 资源字典里的 <c>Icon.*</c>，经 IconKeyToGeometryConverter 解析）。</summary>
    public string IconKey { get; }

    private bool _isSelected;

    /// <summary>选中高亮。画布选中 ↔ 列表行双向同步；行点击经 SelectFromListCommand 写回。</summary>
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public ElementListItemViewModel(AnnotationBase annotation, string summary, string iconKey)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Summary = summary;
        IconKey = iconKey;
    }
}
