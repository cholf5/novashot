# 画布元素选中与 Inspector 属性面板 实施计划

> **给 Claude：** 必需的子技能：使用 com-executing-plans 逐任务实施此计划。

**目标：** 画完即选中（不切工具）+ 左侧元素列表（点选/层序）+ 右侧 Inspector 双态面板（几何/效果/层序按类型展开）。

**架构：** Core 层新增 `ReorderAnnotationCommand` 与 `InspectorGeometry` 纯映射（复用既有 Move/Resize 命令进 Undo）；App 层改 `EditorViewModel` 的 Selection 规则并新增 `ElementList` 集合，`SkiaCanvas` 手柄渲染按工具条件化，`StylePanel` 分组化，新控件 `ElementListPanel`。设计细节见 `docs/plans/2026-11-23-inspector-element-list-design.md`。

**技术栈：** .NET 10 / Avalonia 12 headless / CommunityToolkit.Mvvm / SkiaSharp / xUnit。

**设计修正（相对设计文档）：** `TextAnnotation`/`NumberAnnotation` 只有 `Center` 手柄（见 `HandleKind.cs` 文档），无宽高缩放语义 → 宽/高数值编辑仅支持盒状族（Rectangle/Ellipse/Spotlight/PixelEffect 族）；Text/Number 的 X/Y 仍可编辑，宽高只读。

**关键事实（已核实，实施时直接用）：**
- commit 路径：`SkiaCanvas.OnPointerReleased` → `editor.ExecuteAdd(committed)`（`SkiaCanvas.cs:523-529`）
- 手柄绘制：`DrawOp.Render` 里 `_selection != null` 即调 `DrawSelectionOverlay`（`SkiaCanvas.cs:801-802`）；`DrawOp` 构造已带 `cropToolActive:` 参数可仿照加 `selectToolActive:`
- 编辑区 Grid：`MainWindow.axaml:201` `ColumnDefinitions="*,264"`
- 属性名：`MosaicAnnotation.BlockSize`(4-48)、`BlurAnnotation.Sigma`(2-40)、`NumberAnnotation.Number`(int, private set → `SetNumber`)、`PixelEffectAnnotation.GetHandles()` = 8 向盒子
- 命令：`MoveAnnotationCommand(annotation, delta, document)`、`ResizeAnnotationCommand(annotation, kind, before, after, document)`，构造即可用
- VM 测试模式：`ColorPickerWiringTests`（无需 headless，`MainWindowViewModel(new FakeClipboardService())` + `vm.EnterEditor(Captured())`）；控件实例化测试模式：`CompositionPanelXamlTests`（`[Collection("HeadlessUi")]` + `HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap))`）
- 语言无关断言纪律、i18n 双表同步（`LocKeyConsistencyTests` 兜底）

---

### 任务 1：Core — `AnnotationDocument.Move` + `ReorderAnnotationCommand`

**文件：**
- 修改：`src/Novashot.Core/Annotation/AnnotationDocument.cs`（Insert 方法后）
- 创建：`src/Novashot.Core/Annotation/History/ReorderAnnotationCommand.cs`
- 测试：`tests/Novashot.Core.Tests/AnnotationDocumentMoveTests.cs`（新文件）

**步骤 1：编写失败的测试**

```csharp
using Novashot.Core.Annotation;
using Novashot.Core.Annotation.History;
using Novashot.Core.Annotation.Model;
using SkiaSharp;
using Xunit;

namespace Novashot.Core.Tests;

public class AnnotationDocumentMoveTests
{
    private static AnnotationDocument Doc(int count)
    {
        var doc = new AnnotationDocument(new SKBitmap(10, 10));
        for (int i = 0; i < count; i++)
            doc.Add(new NumberAnnotation(new SKPoint(i, 0), i + 1, AnnotationStyle.Default));
        return doc;
    }

    [Fact]
    public void Move_MiddleToTop_RestacksOrder()
    {
        var doc = Doc(3);
        doc.Move(1, 2); // a1 → 栈顶
        Assert.Equal(0, doc.IndexOf(doc.Annotations[2])); // 原 a1 现在在末位…见下方断言修正
    }
    // 更直接的断言：靠 Id 记录顺序
    [Fact]
    public void Move_MiddleToTop_RestacksById()
    {
        var doc = Doc(3);
        var a1 = doc.Annotations[1];
        doc.Move(1, 2);
        Assert.Equal(2, doc.IndexOf(a1));
    }

    [Fact]
    public void MoveUndo_RestoresOriginalOrder()
    {
        var doc = Doc(3);
        var a1 = doc.Annotations[1];
        var cmd = new ReorderAnnotationCommand(doc, a1, toIndex: 2);
        cmd.Do();
        Assert.Equal(2, doc.IndexOf(a1));
        cmd.Undo();
        Assert.Equal(1, doc.IndexOf(a1));
    }

    [Fact]
    public void Move_SameIndex_NoChangeNoEvent()
    {
        var doc = Doc(2);
        var fired = false;
        doc.Changed += (_, _) => fired = true;
        doc.Move(1, 1);
        Assert.False(fired);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 5)]
    public void Move_OutOfRange_Throws(int from, int to)
    {
        var doc = Doc(2);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Move(from, to));
    }

    [Fact]
    public void Move_TriggersChanged()
    {
        var doc = Doc(2);
        var fired = false;
        doc.Changed += (_, _) => fired = true;
        doc.Move(0, 1);
        Assert.True(fired);
    }
}
```

**步骤 2：运行验证失败**

运行：`dotnet test tests/Novashot.Core.Tests --filter AnnotationDocumentMoveTests`
预期：编译失败（`Move`/`ReorderAnnotationCommand` 不存在）。

**步骤 3：实现**

`AnnotationDocument.cs` 的 `Insert` 方法之后加：

```csharp
/// <summary>
/// 把 <paramref name="fromIndex"/> 处的标注移到 <paramref name="toIndex"/>（其余元素依次滑动）。
/// ReorderAnnotationCommand 的 Do/Undo 通路；from == to 时不动也不触发 Changed。
/// </summary>
public void Move(int fromIndex, int toIndex)
{
    if (fromIndex < 0 || fromIndex >= _annotations.Count)
        throw new ArgumentOutOfRangeException(nameof(fromIndex));
    if (toIndex < 0 || toIndex >= _annotations.Count)
        throw new ArgumentOutOfRangeException(nameof(toIndex));
    if (fromIndex == toIndex) return;
    var a = _annotations[fromIndex];
    _annotations.RemoveAt(fromIndex);
    _annotations.Insert(toIndex, a);
    Changed?.Invoke(this, EventArgs.Empty);
}
```

`ReorderAnnotationCommand.cs`（模式对齐 `RemoveAnnotationCommand`：构造捕获 z-index）：

```csharp
using Novashot.Core.Annotation.Model;

namespace Novashot.Core.Annotation.History;

/// <summary>
/// 调整标注的 z-order。构造时捕获 from 索引，Do = Move(from, to)，Undo = Move(to, from)，
/// 语义幂等。<paramref name="annotation"/> 必须已在文档内（IndexOf &gt;= 0，否则抛）。
/// 注意：若中间隔着其它改 z-order 的操作，索引可能漂移——与 RemoveAnnotationCommand
/// 的 z-index 捕获同款限制。
/// </summary>
public sealed class ReorderAnnotationCommand : IUndoableCommand
{
    private readonly AnnotationDocument _document;
    private readonly AnnotationBase _annotation;
    private readonly int _fromIndex;
    private readonly int _toIndex;

    public string Description => $"Reorder {_annotation.GetType().Name} {_fromIndex} -> {_toIndex}";

    public ReorderAnnotationCommand(AnnotationDocument document, AnnotationBase annotation, int toIndex)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        _fromIndex = document.IndexOf(annotation);
        ArgumentOutOfRangeException.ThrowIfNegative(_fromIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(toIndex, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(toIndex, document.Annotations.Count);
        _toIndex = toIndex;
    }

    public void Do() => _document.Move(_fromIndex, _toIndex);

    public void Undo() => _document.Move(_toIndex, _fromIndex);
}
```

**步骤 4：运行验证通过**

运行：`dotnet test tests/Novashot.Core.Tests --filter AnnotationDocumentMoveTests`
预期：PASS。

---

### 任务 2：Core — `InspectorGeometry` 纯映射

**文件：**
- 创建：`src/Novashot.Core/Annotation/Editing/InspectorGeometry.cs`
- 测试：`tests/Novashot.Core.Tests/InspectorGeometryTests.cs`（新文件）

**步骤 1：编写失败的测试**

```csharp
using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Editing;
using Novashot.Core.Annotation.Model;
using SkiaSharp;
using Xunit;

namespace Novashot.Core.Tests;

public class InspectorGeometryTests
{
    private static RectangleAnnotation Rect(float x, float y, float w, float h)
        => new(new SKRect(x, y, x + w, y + h), AnnotationStyle.Default);

    // ---- SupportsResize：盒状族 true，其余 false ----

    [Fact]
    public void SupportsResize_BoxFamily_True()
    {
        Assert.True(InspectorGeometry.SupportsResize(Rect(0, 0, 5, 5)));
        Assert.True(InspectorGeometry.SupportsResize(new EllipseAnnotation(new SKRect(0, 0, 5, 5), AnnotationStyle.Default)));
        Assert.True(InspectorGeometry.SupportsResize(
            new MosaicAnnotation(new SKRect(0, 0, 5, 5), new SKBitmap(10, 10), AnnotationStyle.Default)));
    }

    [Fact]
    public void SupportsResize_TextNumberOpenShapes_False()
    {
        Assert.False(InspectorGeometry.SupportsResize(
            new TextAnnotation("hi", new SKPoint(0, 0), AnnotationStyle.Default)));
        Assert.False(InspectorGeometry.SupportsResize(
            new NumberAnnotation(new SKPoint(5, 5), 1, AnnotationStyle.Default)));
        Assert.False(InspectorGeometry.SupportsResize(
            new LineAnnotation(new SKPoint(0, 0), new SKPoint(9, 9), AnnotationStyle.Default)));
    }

    // ---- MoveDelta：clamp 到画布内 ----

    [Fact]
    public void MoveDelta_FreeMove_ReturnsDelta()
    {
        var d = InspectorGeometry.MoveDelta(new SKRect(2, 2, 6, 6), 4, 5, 100, 100);
        Assert.Equal(2f, d.X);
        Assert.Equal(3f, d.Y);
    }

    [Fact]
    public void MoveDelta_OutOfCanvas_Clamps()
    {
        var d = InspectorGeometry.MoveDelta(new SKRect(2, 2, 6, 6), -5, 99, 100, 100);
        Assert.Equal(-2f, d.X);  // left >= 0
        Assert.Equal(94f, d.Y);  // top <= 100 - 4(width) = 96？注意：bbox.Width=4 → maxTop=96
    }

    // ---- ResizeTarget：SE 目标点，下限 4、不越画布 ----

    [Fact]
    public void ResizeTarget_NormalSize_ReturnsCorner()
    {
        var t = InspectorGeometry.ResizeTarget(new SKRect(2, 2, 6, 6), 10, 8, 100, 100);
        Assert.Equal(new SKPoint(12, 10), t);
    }

    [Fact]
    public void ResizeTarget_TooSmall_ClampsToMin()
    {
        var t = InspectorGeometry.ResizeTarget(new SKRect(2, 2, 6, 6), 1, 1, 100, 100);
        Assert.Equal(new SKPoint(6, 6), t); // w=h=MinSize(4)
    }

    [Fact]
    public void ResizeTarget_BeyondCanvas_ClampsToImageEdge()
    {
        var t = InspectorGeometry.ResizeTarget(new SKRect(2, 2, 6, 6), 500, 500, 100, 100);
        Assert.Equal(new SKPoint(100, 100), t);
    }
}
```

注：`TextAnnotation`/`EllipseAnnotation`/`LineAnnotation` 的构造签名以实际代码为准（先读 `Model/` 下对应文件再写测试，别照抄参数顺序）。

**步骤 2：运行验证失败**

运行：`dotnet test tests/Novashot.Core.Tests --filter InspectorGeometryTests`
预期：编译失败（类型不存在）。

**步骤 3：实现**

```csharp
using Novashot.Core.Annotation.Model;
using SkiaSharp;

namespace Novashot.Core.Annotation.Editing;

/// <summary>
/// Inspector 几何数值编辑的纯映射：把 X/Y/宽/高 输入翻译成既有 Move/Resize 命令的参数。
/// 权限矩阵：X/Y 所有类型可编辑（MoveBy 全类型通用）；
/// 宽/高仅盒状族（8 向盒子手柄语义）；Text/Number 只有 Center 手柄、开放形状是端点/路径，
/// 宽高一律只读。
/// </summary>
public static class InspectorGeometry
{
    /// <summary>宽/高编辑下限（像素）。</summary>
    public const float MinSize = 4f;

    /// <summary>该标注是否支持宽/高数值编辑（需要 8 向盒子手柄）。</summary>
    public static bool SupportsResize(AnnotationBase a) => a is
        RectangleAnnotation or EllipseAnnotation or SpotlightAnnotation or PixelEffectAnnotation;

    /// <summary>
    /// X/Y 输入 → <see cref="History.MoveAnnotationCommand"/> 的 delta（bbox 左上角移到 clamp 后目标点）。
    /// 目标点 clamp：bbox 完整落在 [0, imageW/H] 内（bbox 比画布大时贴边）。
    /// </summary>
    public static SKPoint MoveDelta(SKRect bbox, float newX, float newY, int imageW, int imageH)
    {
        var maxLeft = Math.Max(0f, imageW - bbox.Width);
        var maxTop = Math.Max(0f, imageH - bbox.Height);
        var left = Math.Clamp(newX, 0f, maxLeft);
        var top = Math.Clamp(newY, 0f, maxTop);
        return new SKPoint(left - bbox.Left, top - bbox.Top);
    }

    /// <summary>
    /// 宽/高输入 → <see cref="History.ResizeAnnotationCommand"/> 的 BottomRight 目标点
    /// （clamp：下限 <see cref="MinSize"/>，右/下边不出画布）。
    /// </summary>
    public static SKPoint ResizeTarget(SKRect bbox, float newW, float newH, int imageW, int imageH)
    {
        var w = Math.Clamp(newW, MinSize, Math.Max(MinSize, imageW - bbox.Left));
        var h = Math.Clamp(newH, MinSize, Math.Max(MinSize, imageH - bbox.Top));
        return new SKPoint(bbox.Left + w, bbox.Top + h);
    }
}
```

**步骤 4：运行验证通过**

运行：`dotnet test tests/Novashot.Core.Tests --filter InspectorGeometryTests`
预期：PASS。

---

### 任务 3：App — `EditorViewModel`：即选中 + 跨工具 Selection + 层序命令

**文件：**
- 修改：`src/Novashot.App/ViewModels/EditorViewModel.cs`
- 测试：`tests/Novashot.App.Tests/EditorSelectionRulesTests.cs`（新文件，VM 级无需 headless，仿 `ColorPickerWiringTests`）

**步骤 1：编写失败的测试**

```csharp
using Novashot.App.ViewModels;
using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Annotation.Tools;
using Novashot.Core.Capture;
using SkiaSharp;
using Xunit;

namespace Novashot.App.Tests;

/// <summary>Inspector 交互规则：画完即选中、Selection 跨工具存活、层序命令进 Undo。</summary>
public class EditorSelectionRulesTests
{
    private static CapturedImage Captured(int w = 64, int h = 48)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new CapturedImage(bmp, new Novashot.Core.Common.PixelRect(0, 0, w, h), 1.0, CaptureSource.Region);
    }

    private static EditorViewModel Editor()
    {
        var vm = new MainWindowViewModel(new FakeClipboardService());
        vm.EnterEditor(Captured());
        return vm.Editor!;
    }

    private static RectangleAnnotation Rect(float x, float y)
        => new(new SKRect(x, y, x + 10, y + 10), AnnotationStyle.Default);

    [Fact]
    public void ExecuteAdd_SetsSelection_AndKeepsTool()
    {
        var editor = Editor();
        editor.CurrentTool = ToolKind.Rectangle;
        var rect = Rect(0, 0);
        editor.ExecuteAdd(rect);
        Assert.Same(rect, editor.Selection);
        Assert.Equal(ToolKind.Rectangle, editor.CurrentTool); // 不切走
    }

    [Fact]
    public void SwitchTool_KeepsSelection()
    {
        var editor = Editor();
        editor.ExecuteAdd(Rect(0, 0));
        editor.CurrentTool = ToolKind.Ellipse;
        Assert.NotNull(editor.Selection); // 不再清空
    }

    [Fact]
    public void SelectToolEmptyClick_ClearsSelection_StillWorks()
    {
        var editor = Editor();
        editor.ExecuteAdd(Rect(0, 0));
        var tool = (SelectTool)editor.CreateActiveTool();
        tool.OnPointerDown(new SKPoint(50, 40), editor.CurrentStyle); // 空白处
        Assert.Null(editor.Selection);
    }

    [Fact]
    public void BringForward_ThenUndo_RestoresOrder()
    {
        var editor = Editor();
        var a = Rect(0, 0);
        var b = Rect(5, 5);
        editor.ExecuteAdd(a);
        editor.ExecuteAdd(b);
        Assert.Equal(1, editor.Document.IndexOf(b)); // 栈顶
        editor.Selection = a;
        editor.BringForwardCommand.Execute(null);
        Assert.Equal(1, editor.Document.IndexOf(a));
        editor.UndoCommand.Execute(null);
        Assert.Equal(0, editor.Document.IndexOf(a));
    }

    [Fact]
    public void BringForward_OnTopSelection_IsDisabled()
    {
        var editor = Editor();
        editor.ExecuteAdd(Rect(0, 0)); // 唯一且在栈顶
        Assert.False(editor.BringForwardCommand.CanExecute(null));
        Assert.False(editor.BringToFrontCommand.CanExecute(null));
        Assert.False(editor.SendBackwardCommand.CanExecute(null));
        Assert.False(editor.SendToBackCommand.CanExecute(null));
    }

    [Fact]
    public void SendToBack_MovesToIndex0()
    {
        var editor = Editor();
        var a = Rect(0, 0);
        editor.ExecuteAdd(a);
        editor.ExecuteAdd(Rect(5, 5));
        editor.ExecuteAdd(Rect(10, 10));
        editor.Selection = a;
        editor.SendToBackCommand.Execute(null);
        Assert.Equal(0, editor.Document.IndexOf(a));
    }
}
```

**步骤 2：运行验证失败**

运行：`dotnet test tests/Novashot.App.Tests -f net10.0 --filter EditorSelectionRulesTests`
预期：编译失败（`BringForwardCommand` 不存在 / `ExecuteAdd` 未设 Selection）。

**步骤 3：实现 `EditorViewModel.cs`**

1. 删除 `OnCurrentToolChanged` 整个 partial 方法（切工具清空逻辑）；`CreateActiveTool` 与 SelectTool 相关代码不动。
2. `ExecuteAdd` 设选中：

```csharp
public void ExecuteAdd(AnnotationBase annotation)
{
    ArgumentNullException.ThrowIfNull(annotation);
    History.Execute(new AddAnnotationCommand(Document, annotation));
    // 画完即选中：Selection 跨工具存活，Inspector 立即反映新标注；工具不切走。
    Selection = annotation;
}
```

3. 新命令（放在 `DeselectCommand` 声明附近）：

```csharp
public RelayCommand BringForwardCommand { get; }
public RelayCommand SendBackwardCommand { get; }
public RelayCommand BringToFrontCommand { get; }
public RelayCommand SendToBackCommand { get; }
```

构造函数里：

```csharp
BringForwardCommand = new RelayCommand(
    execute: () => ReorderSelection(+1),
    canExecute: () => CanReorder(+1));
SendBackwardCommand = new RelayCommand(
    execute: () => ReorderSelection(-1),
    canExecute: () => CanReorder(-1));
BringToFrontCommand = new RelayCommand(
    execute: () => ReorderSelection(toTop: true),
    canExecute: () => CanReorder(toTop: true));
SendToBackCommand = new RelayCommand(
    execute: () => ReorderSelection(toTop: false),
    canExecute: () => CanReorder(toTop: false));
```

私有实现：

```csharp
private bool CanReorder(int step) => CanReorderCore(Selection is null ? null : Document.IndexOf(Selection) + step);
private bool CanReorder(bool toTop) => CanReorderCore(toTop ? Document.Annotations.Count - 1 : 0);
private bool CanReorderCore(int? target)
    => Selection is not null && Document.IndexOf(Selection) is var from && from >= 0
       && target is int t && t >= 0 && t < Document.Annotations.Count && t != from;

private void ReorderSelection(int step) => ReorderCore(Document.IndexOf(Selection!) + step);
private void ReorderSelection(bool toTop) => ReorderCore(toTop ? Document.Annotations.Count - 1 : 0);
private void ReorderCore(int toIndex)
{
    var sel = Selection;
    if (sel is null) return;
    var from = Document.IndexOf(sel);
    if (from < 0 || from == toIndex || toIndex < 0 || toIndex >= Document.Annotations.Count) return;
    History.Execute(new ReorderAnnotationCommand(Document, sel, toIndex));
}
```

`OnSelectionChanged` 里追加 `NotifyCanExecuteChanged` 四连：

```csharp
partial void OnSelectionChanged(AnnotationBase? value)
{
    DeleteSelectionCommand.NotifyCanExecuteChanged();
    BringForwardCommand.NotifyCanExecuteChanged();
    SendBackwardCommand.NotifyCanExecuteChanged();
    BringToFrontCommand.NotifyCanExecuteChanged();
    SendToBackCommand.NotifyCanExecuteChanged();
}
```

文件顶部补 `using Novashot.Core.Annotation.History;`（已有则免）。

**步骤 4：运行验证通过**

运行：`dotnet test tests/Novashot.App.Tests -f net10.0 --filter EditorSelectionRulesTests`
预期：PASS。

**步骤 5：回归相邻测试**

运行：`dotnet test tests/Novashot.App.Tests -f net10.0`（确认删掉切工具清空没破坏既有用例，尤其 `SelectionGeometryTests`、`UiSnapshotTests` 默认腿）

---

### 任务 4：App — `ElementList` 集合与列表项 VM

**文件：**
- 创建：`src/Novashot.App/ViewModels/ElementListItemViewModel.cs`
- 修改：`src/Novashot.App/ViewModels/EditorViewModel.cs`
- 测试：`tests/Novashot.App.Tests/ElementListViewModelTests.cs`（新文件）

**步骤 1：编写失败的测试**

```csharp
using Novashot.App.ViewModels;
using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Annotation.Tools;
using Novashot.Core.Capture;
using SkiaSharp;
using Xunit;

namespace Novashot.App.Tests;

public class ElementListViewModelTests
{
    private static EditorViewModel Editor()
    {
        var vm = new MainWindowViewModel(new FakeClipboardService());
        var bmp = new SKBitmap(new SKImageInfo(64, 48, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        vm.EnterEditor(new CapturedImage(bmp, new Novashot.Core.Common.PixelRect(0, 0, 64, 48), 1.0, CaptureSource.Region));
        return vm.Editor!;
    }

    [Fact]
    public void ExecuteAdd_AppendsItem_TopFirst()
    {
        var editor = Editor();
        var a = new RectangleAnnotation(new SKRect(0, 0, 10, 10), AnnotationStyle.Default);
        var b = new RectangleAnnotation(new SKRect(5, 5, 15, 15), AnnotationStyle.Default);
        editor.ExecuteAdd(a);
        editor.ExecuteAdd(b);
        Assert.Equal(2, editor.ElementList.Count);
        Assert.Same(b, editor.ElementList[0].Annotation); // 栈顶在列表顶
        Assert.Same(a, editor.ElementList[1].Annotation);
    }

    [Fact]
    public void Remove_SyncsList()
    {
        var editor = Editor();
        var a = new RectangleAnnotation(new SKRect(0, 0, 10, 10), AnnotationStyle.Default);
        editor.ExecuteAdd(a);
        editor.DeleteSelectionCommand.Execute(null);
        Assert.Empty(editor.ElementList);
        Assert.False(editor.HasElements);
    }

    [Fact]
    public void SelectionChange_SyncsHighlight()
    {
        var editor = Editor();
        var a = new RectangleAnnotation(new SKRect(0, 0, 10, 10), AnnotationStyle.Default);
        editor.ExecuteAdd(a);
        editor.Selection = null;
        Assert.False(editor.ElementList[0].IsSelected);
        editor.Selection = a;
        Assert.True(editor.ElementList[0].IsSelected);
    }

    [Fact]
    public void Summary_ByType()
    {
        var editor = Editor();
        editor.ExecuteAdd(new RectangleAnnotation(new SKRect(0, 0, 10, 10), AnnotationStyle.Default));
        editor.ExecuteAdd(new TextAnnotation("hello world 123", new SKPoint(0, 20), AnnotationStyle.Default));
        editor.ExecuteAdd(new NumberAnnotation(new SKPoint(5, 5), 3, AnnotationStyle.Default));
        Assert.Equal(Loc.T("Kind.Rectangle"), editor.ElementList[2].Summary);
        Assert.Equal("hello world 123", editor.ElementList[1].Summary[..Math.Min(12, 14)]);
        Assert.Equal("#3", editor.ElementList[0].Summary);
    }

    [Fact]
    public void SelectFromListCommand_Selects_AndSwitchesToSelectTool()
    {
        var editor = Editor();
        editor.CurrentTool = ToolKind.Mosaic;
        var a = new RectangleAnnotation(new SKRect(0, 0, 10, 10), AnnotationStyle.Default);
        editor.ExecuteAdd(a);
        editor.CurrentTool = ToolKind.Mosaic; // ExecuteAdd 不切工具
        editor.SelectFromListCommand.Execute(editor.ElementList[0]);
        Assert.Same(a, editor.Selection);
        Assert.Equal(ToolKind.Select, editor.CurrentTool);
    }
}
```

注：`Loc.T` 引用按 `ColorPickerWiringTests` 的 using 方式补 `using Novashot.App.Localization;`；`TextAnnotation` 构造签名以实际代码为准。

**步骤 2：运行验证失败**

运行：`dotnet test tests/Novashot.App.Tests -f net10.0 --filter ElementListViewModelTests`
预期：编译失败。

**步骤 3：实现**

`ElementListItemViewModel.cs`：

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Novashot.Core.Annotation.Model;

namespace Novashot.App.ViewModels;

/// <summary>
/// 左侧元素列表的单行。列表顺序 = z-order（栈顶在顶）；摘要与图标在构造时定
/// （标注文本改动经 Document.Changed 重建整表，不需要行内刷新）。
/// </summary>
public sealed class ElementListItemViewModel : ObservableObject
{
    public AnnotationBase Annotation { get; }

    /// <summary>行摘要：Text→内容截 12 字、Number→#序号、其余→类型名（Loc）。</summary>
    public string Summary { get; }

    /// <summary>图标资源 key（MainWindow 资源字典里的 Icon.*）。</summary>
    public string IconKey { get; }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public ElementListItemViewModel(AnnotationBase annotation, string summary, string iconKey)
    {
        Annotation = annotation;
        Summary = summary;
        IconKey = iconKey;
    }
}
```

`EditorViewModel.cs` 追加（成员区 + 方法区）：

```csharp
/// <summary>左侧元素列表（栈顶在顶）。Document.Changed 全量重建（截图标注量级小，简单可靠）。</summary>
public System.Collections.ObjectModel.ObservableCollection<ElementListItemViewModel> ElementList { get; } = new();

/// <summary>列表非空（左栏显隐用）。</summary>
public bool HasElements => ElementList.Count > 0;

public CommunityToolkit.Mvvm.Input.RelayCommand<ElementListItemViewModel?> SelectFromListCommand { get; }
```

构造函数：

```csharp
SelectFromListCommand = new RelayCommand<ElementListItemViewModel?>(
    execute: item =>
    {
        if (item is null) return;
        Selection = item.Annotation;
        CurrentTool = ToolKind.Select; // 点列表 = 「我要操作它」的强意图
    });
```

`OnDocumentChanged` 末尾调 `RebuildElementList()`；`OnSelectionChanged` 调 `SyncListSelection()`：

```csharp
private void RebuildElementList()
{
    ElementList.Clear();
    // Annotations 是底→顶；列表顶 = 栈顶，故逆序填充
    for (int i = Document.Annotations.Count - 1; i >= 0; i--)
    {
        var a = Document.Annotations[i];
        ElementList.Add(new ElementListItemViewModel(a, Summarize(a), IconKeyOf(a)));
    }
    OnPropertyChanged(nameof(HasElements));
    SyncListSelection();
}

private void SyncListSelection()
{
    foreach (var item in ElementList)
        item.IsSelected = ReferenceEquals(item.Annotation, Selection);
}

private static string Summarize(AnnotationBase a) => a switch
{
    TextAnnotation t => t.Text.Length <= 12 ? t.Text : t.Text[..12],
    NumberAnnotation n => $"#{n.Number}",
    _ => Loc.KindLabel(a), // 见下：把 StylePanelViewModel.SelectionKindLabel 的映射下沉为可复用
};

private static string IconKeyOf(AnnotationBase a) => a switch
{
    RectangleAnnotation    => "Icon.Rect",
    EllipseAnnotation      => "Icon.Ellipse",
    LineAnnotation         => "Icon.Line",
    ArrowStraightAnnotation => "Icon.Arrow",
    ArrowCurvedAnnotation  => "Icon.Curve",
    TextAnnotation         => "Icon.Text",
    NumberAnnotation       => "Icon.Number",
    SpotlightAnnotation    => "Icon.Spotlight",
    PencilAnnotation       => "Icon.Pencil",
    HighlighterAnnotation  => "Icon.Highlighter",
    MosaicAnnotation       => "Icon.Mosaic",
    BlurAnnotation         => "Icon.Blur",
    InvertAnnotation       => "Icon.Invert",
    _                      => "Icon.Rect",
};
```

**`Loc.KindLabel` 下沉**：把 `StylePanelViewModel.SelectionKindLabel` 的 switch 迁到 `Loc.cs`：

```csharp
public static string KindLabel(AnnotationBase? a) => a switch
{
    null                    => T("Kind.None"),
    RectangleAnnotation _   => T("Kind.Rectangle"),
    // ……原样搬 StylePanelViewModel.SelectionKindLabel 的全部分支
};
```

`StylePanelViewModel.SelectionKindLabel` 改为 `=> Loc.KindLabel(Editor.Selection);`（行为不变）。

**步骤 4：运行验证通过**

运行：`dotnet test tests/Novashot.App.Tests -f net10.0 --filter ElementListViewModelTests`
预期：PASS。

---

### 任务 5：App — `SkiaCanvas` 手柄条件化 + 淡选中框

**文件：**
- 修改：`src/Novashot.App/Controls/SkiaCanvas.cs`（`Render`/`DrawOp` 构造/字段/`DrawSelectionOverlay`）

无直接单测（纯渲染）；验证 = 构建零警告 + 任务 8 快照目检 + 任务 3 的 `SelectToolEmptyClick_ClearsSelection_StillWorks` 等行为测试不回归。

**步骤 1：`DrawOp` 增加工具标志**

`SkiaCanvas.Render`（`SkiaCanvas.cs:207-217`）构造 `DrawOp` 的调用处追加：

```csharp
selectToolActive: editor.CurrentTool == ToolKind.Select
```

`DrawOp`：新增私有字段 `private readonly bool _selectToolActive;`、构造参数 `bool selectToolActive`（排在 `cropToolActive` 后）。

**步骤 2：渲染分流**

`DrawOp.Render` 的选中分支（原 `SkiaCanvas.cs:801-802`）改为：

```csharp
if (_selection is not null && !ReferenceEquals(_selection, _editingText))
{
    if (_selectToolActive)
        DrawSelectionOverlay(canvas, _selection); // Select 工具：白虚线框 + 手柄
    else
        DrawSoftSelectionBox(canvas, _selection); // 绘制工具：淡虚线框，无手柄、不参与命中
}
```

新方法（放在 `DrawSelectionOverlay` 后）：

```csharp
/// <summary>
/// 绘制工具下的淡选中框：选中色 60% 透明、1.5px 虚线、无手柄。纯视觉，
/// SkiaCanvas 的指针命中不经过它，画笔落点不受影响。
/// </summary>
private void DrawSoftSelectionBox(SKCanvas canvas, AnnotationBase selection)
{
    var c = selection.Style.Stroke;
    using var outline = new SKPaint
    {
        Color = new SKColor(c.Red, c.Green, c.Blue, 0x99), // ~60% alpha
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        IsAntialias = true,
        PathEffect = SKPathEffect.CreateDash(new float[] { 4f, 3f }, 0f),
    };
    canvas.DrawRect(_matrix.MapRect(ImageRectToContent(selection.BoundingBox)), outline);
}
```

**步骤 3：构建验证**

运行：`dotnet build Novashot.slnx`
预期：0 警告 0 错误（`TreatWarningsAsErrors`）。

---

### 任务 6：App — `StylePanelViewModel` 几何/效果/双态扩展

**文件：**
- 修改：`src/Novashot.App/ViewModels/StylePanelViewModel.cs`
- 测试：`tests/Novashot.App.Tests/StylePanelInspectorTests.cs`（新文件）

**步骤 1：编写失败的测试**

```csharp
using Novashot.App.ViewModels;
using Novashot.Core.Annotation;
using Novashot.Core.Annotation.History;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Capture;
using SkiaSharp;
using Xunit;

namespace Novashot.App.Tests;

/// <summary>Inspector 双态与几何/效果编辑。</summary>
public class StylePanelInspectorTests
{
    private static StylePanelViewModel Panel(out EditorViewModel editor)
    {
        var vm = new MainWindowViewModel(new FakeClipboardService());
        var bmp = new SKBitmap(new SKImageInfo(64, 48, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        vm.EnterEditor(new CapturedImage(bmp, new Novashot.Core.Common.PixelRect(0, 0, 64, 48), 1.0, CaptureSource.Region));
        editor = vm.Editor!;
        return editor.StylePanel;
    }

    private static RectangleAnnotation Rect(float x, float y, float w = 10, float h = 10)
        => new(new SKRect(x, y, x + w, y + h), AnnotationStyle.Default);

    [Fact]
    public void NoSelection_HasSelectionFalse_GeometryHidden()
    {
        var panel = Panel(out var editor);
        Assert.False(panel.HasSelection);
        Assert.False(panel.IsGeometryVisible);
    }

    [Fact]
    public void BoxSelection_GeometryVisible_SizeEditable()
    {
        var panel = Panel(out var editor);
        editor.ExecuteAdd(Rect(2, 3));
        Assert.True(panel.HasSelection);
        Assert.True(panel.IsGeometryVisible);
        Assert.True(panel.IsSizeEditable);
        Assert.Equal(2f, panel.SelX);
        Assert.Equal(3f, panel.SelY);
        Assert.Equal(10f, panel.SelW);
        Assert.Equal(10f, panel.SelH);
    }

    [Fact]
    public void SetSelX_MovesAnnotation_AndIsUndoable()
    {
        var panel = Panel(out var editor);
        var rect = Rect(2, 3);
        editor.ExecuteAdd(rect);
        panel.SelX = 20;
        Assert.Equal(20f, rect.BoundingBox.Left);
        editor.UndoCommand.Execute(null);
        Assert.Equal(2f, rect.BoundingBox.Left);
    }

    [Fact]
    public void SetSelW_ResizesViaBottomRight_AndClamps()
    {
        var panel = Panel(out var editor);
        var rect = Rect(2, 3);
        editor.ExecuteAdd(rect);
        panel.SelW = 30;
        Assert.Equal(30f, rect.BoundingBox.Width);
        panel.SelW = 0.5f; // 低于 MinSize → 不动
        Assert.Equal(30f, rect.BoundingBox.Width);
    }

    [Fact]
    public void TextSelection_SizeNotEditable_ButXYIs()
    {
        var panel = Panel(out var editor);
        editor.ExecuteAdd(new TextAnnotation("hi", new SKPoint(4, 4), AnnotationStyle.Default));
        Assert.True(panel.IsGeometryVisible);
        Assert.False(panel.IsSizeEditable);
    }

    [Fact]
    public void MosaicSelection_ShowsBlockSize()
    {
        var panel = Panel(out var editor);
        editor.ExecuteAdd(new MosaicAnnotation(
            new SKRect(0, 0, 20, 20), editor.Document.Source, AnnotationStyle.Default));
        Assert.True(panel.IsEffectVisible);
        Assert.Equal(MosaicAnnotation.DefaultBlockSize, panel.EffectStrength);
        panel.EffectStrength = 24;
        Assert.Equal(24, ((MosaicAnnotation)editor.Selection!).BlockSize);
    }

    [Fact]
    public void GeometryRefresh_AfterMove()
    {
        var panel = Panel(out var editor);
        var rect = Rect(2, 3);
        editor.ExecuteAdd(rect);
        rect.MoveBy(new SKPoint(5, 0)); // 面板外改动（如 undo 后）也要反映
        Assert.Equal(7f, panel.SelX);
    }
}
```

注：`MosaicAnnotation`/`TextAnnotation` 构造签名以实际代码为准。

**步骤 2：运行验证失败**

运行：`dotnet test tests/Novashot.App.Tests -f net10.0 --filter StylePanelInspectorTests`
预期：编译失败。

**步骤 3：实现 `StylePanelViewModel.cs`**

1. 订阅 `Editor.Document.Changed`（构造订阅、`Dispose` 退订），触发几何属性刷新：

```csharp
private void OnDocumentChanged(object? sender, EventArgs e)
{
    OnPropertyChanged(nameof(SelX));
    OnPropertyChanged(nameof(SelY));
    OnPropertyChanged(nameof(SelW));
    OnPropertyChanged(nameof(SelH));
    OnPropertyChanged(nameof(HasSelection));
    OnPropertyChanged(nameof(IsGeometryVisible));
    OnPropertyChanged(nameof(IsSizeEditable));
    OnPropertyChanged(nameof(IsEffectVisible));
    OnPropertyChanged(nameof(EffectStrength));
    OnPropertyChanged(nameof(EffectMin));
    OnPropertyChanged(nameof(EffectMax));
}
```

`OnEditorPropertyChanged` 的属性名列表追加 `HasSelection` 等（Selection 变化时刷新全部新属性，可直接调 `OnDocumentChanged` 逻辑抽出的 `RefreshInspector()`）。

2. 新属性（`SelectionKindLabel` 改为 `Loc.KindLabel(Editor.Selection)`）：

```csharp
public bool HasSelection => Editor.Selection is not null;
public bool IsGeometryVisible => Editor.Selection is not null;
public bool IsSizeEditable => Editor.Selection is { } s && InspectorGeometry.SupportsResize(s);
public bool IsEffectVisible => Editor.Selection is MosaicAnnotation or BlurAnnotation;
public float EffectMin => Editor.Selection is BlurAnnotation ? BlurAnnotation.MinSigma : MosaicAnnotation.MinBlockSize;
public float EffectMax => Editor.Selection is BlurAnnotation ? BlurAnnotation.MaxSigma : MosaicAnnotation.MaxBlockSize;

public float SelX
{
    get => Editor.Selection?.BoundingBox.Left ?? 0f;
    set
    {
        if (Editor.Selection is not { } sel) return;
        var bbox = sel.BoundingBox;
        var delta = InspectorGeometry.MoveDelta(bbox, value, bbox.Top, Editor.Document.Width, Editor.Document.Height);
        if (delta.X != 0 || delta.Y != 0)
            Editor.History.Execute(new MoveAnnotationCommand(sel, delta, Editor.Document));
        RefreshInspector();
    }
}
public float SelY  // 同构：MoveDelta(bbox, bbox.Left, value, …)
public float SelW
{
    get => Editor.Selection?.BoundingBox.Width ?? 0f;
    set
    {
        if (Editor.Selection is not { } sel || !IsSizeEditable) return;
        var bbox = sel.BoundingBox;
        var target = InspectorGeometry.ResizeTarget(bbox, value, bbox.Height, Editor.Document.Width, Editor.Document.Height);
        if (target != bbox.BottomRight)
            Editor.History.Execute(new ResizeAnnotationCommand(sel, HandleKind.BottomRight, bbox.BottomRight, target, Editor.Document));
        RefreshInspector();
    }
}
public float SelH  // 同构：ResizeTarget(bbox, bbox.Width, value, …)

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
                if (nextBlock != m.BlockSize) { m.BlockSize = nextBlock; Editor.Document.NotifyMutated(); }
                break;
            case BlurAnnotation b:
                var nextSigma = Math.Clamp(value, BlurAnnotation.MinSigma, BlurAnnotation.MaxSigma);
                if (Math.Abs(nextSigma - b.Sigma) > 0.001f) { b.Sigma = nextSigma; Editor.Document.NotifyMutated(); }
                break;
        }
    }
}
```

`RefreshInspector()` = 上面 `OnDocumentChanged` 的通知集合抽成私有方法，两处调用。

using 补：`Novashot.Core.Annotation.Model`、`Novashot.Core.Annotation.History`、`Novashot.Core.Annotation.Editing`。

**步骤 4：运行验证通过 + 回归**

运行：`dotnet test tests/Novashot.App.Tests -f net10.0 --filter StylePanelInspectorTests` → PASS
运行：`dotnet test tests/Novashot.App.Tests -f net10.0`（既有 `SettingsViewModelTests` 等不回归）

---

### 任务 7：App — `ElementListPanel` 控件 + 面板分组 + 三列布局 + i18n

**文件：**
- 创建：`src/Novashot.App/Controls/ElementListPanel.axaml` + `.axaml.cs`
- 创建：`src/Novashot.App/Converters/IconKeyToGeometryConverter.cs`
- 修改：`src/Novashot.App/Views/MainWindow.axaml`（`:201` 三列 + 插入左栏；资源区注册转换器）
- 修改：`src/Novashot.App/Controls/StylePanel.axaml`（分组化）
- 修改：`src/Novashot.App/Localization/Loc.cs`（双表加 key）
- 测试：`tests/Novashot.App.Tests/ElementListPanelXamlTests.cs`（新文件，仿 `CompositionPanelXamlTests`）

**步骤 1：i18n keys（两表同步加）**

En / Zh 各加（key 相同、占位符无）：

```
Panel.Elements   "Elements"        / "元素"
Panel.Layer      "Order"           / "层序"
Panel.Up         "Move up"         / "上移"
Panel.Down       "Move down"       / "下移"
Panel.ToTop      "Bring to front"  / "置顶"
Panel.ToBottom   "Send to back"    / "置底"
Panel.Geometry   "Geometry"        / "几何"
Panel.Width      "W"               / "宽"
Panel.Height     "H"               / "高"
Panel.Effects    "Effects"         / "效果"
Panel.BlockSize  "Pixel size"      / "颗粒度"
Panel.Strength   "Strength"        / "强度"
Panel.GeometryReadonlyHint "Size editing not available for this shape" / "此类型不支持宽高编辑"
```

**步骤 2：编写失败的测试**

```csharp
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Novashot.App.Controls;
using Novashot.App.ViewModels;
using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Capture;
using SkiaSharp;
using Xunit;

namespace Novashot.App.Tests;

[Collection("HeadlessUi")]
public class ElementListPanelXamlTests
{
    [Fact]
    public async Task ElementListPanel_RealizesRows_AndClickSelects()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        System.Func<System.Threading.Tasks.Task> act = async () =>
        {
            var bmp = new SKBitmap(new SKImageInfo(64, 48, SKColorType.Bgra8888, SKAlphaType.Opaque));
            bmp.Erase(SKColors.DimGray);
            var vm = new MainWindowViewModel(new FakeClipboardService());
            vm.EnterEditor(new CapturedImage(bmp, new Novashot.Core.Common.PixelRect(0, 0, 64, 48), 1.0, CaptureSource.Region));
            var editor = vm.Editor!;
            editor.ExecuteAdd(new RectangleAnnotation(new SKRect(0, 0, 10, 10), AnnotationStyle.Default));
            editor.ExecuteAdd(new MosaicAnnotation(new SKRect(5, 5, 25, 25), editor.Document.Source, AnnotationStyle.Default));

            var panel = new ElementListPanel { DataContext = editor };
            var window = new Window { Width = 400, Height = 600, Content = panel };
            window.Show();
            window.UpdateLayout();
            window.UpdateLayout();

            var list = panel.FindControl<ItemsControl>("ElementListItems");
            Assert.NotNull(list);
            Assert.Equal(2, list!.ItemCount);
            Assert.True(panel.IsVisible);

            // 点首行（栈顶 Mosaic）→ 选中 + 切 Select 工具
            editor.SelectFromListCommand.Execute(editor.ElementList[0]);
            Assert.Equal(ToolKind.Select, editor.CurrentTool);
            Assert.True(editor.ElementList[0].IsSelected);

            // 清空文档 → 整栏隐藏
            editor.Document.Clear();
            window.UpdateLayout();
            Assert.False(panel.IsVisible);
            window.Close();
        };
        await session.Dispatch(act, System.Threading.CancellationToken.None);
    }
}
```

**步骤 3：运行验证失败**

运行：`dotnet test tests/Novashot.App.Tests -f net10.0-windows10.0.19041.0 --filter ElementListPanelXamlTests`
预期：编译失败（控件不存在）。

**步骤 4：实现控件**

`ElementListPanel.axaml` 结构（样式 token 沿用 `Bg.Panel`/`Line`/`Ink.2`，细节对齐 `StylePanel.axaml` 的既有写法）：

```xml
<UserControl xmlns="https://github.com/avaloniaui" …
             x:Class="Novashot.App.Controls.ElementListPanel"
             x:DataType="vm:EditorViewModel">
  <StackPanel Margin="8,10,8,4" Spacing="8">
    <!-- 标题行 + 折叠按钮 -->
    <Grid ColumnDefinitions="*,Auto">
      <TextBlock Classes="sec" Text="{DynamicResource L.Panel.Elements}" />
      <ToggleButton x:Name="CollapseToggle" Classes="icon" IsChecked="True" />
    </Grid>
    <ItemsControl x:Name="ElementListItems" ItemsSource="{Binding ElementList}"
                  IsVisible="{Binding #CollapseToggle.IsChecked}">
      <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="vm:ElementListItemViewModel">
          <!-- Q-1 规矩：IsChecked OneWay 回显 + Click 携带状态，不用 TwoWay -->
          <RadioButton Classes="element-row" GroupName="ElementRows"
                       IsChecked="{Binding IsSelected, Mode=OneWay}"
                       Click="OnRowClick" Tag="{Binding}">
            <Grid ColumnDefinitions="Auto,*,Auto">
              <Path Classes="glyph" Width="14" Height="14" Margin="0,0,8,0"
                    Data="{Binding IconKey, Converter={StaticResource IconKeyToGeometry}}"
                    Stroke="{Binding $parent[RadioButton].Foreground}" />
              <TextBlock Grid.Column="1" Text="{Binding Summary}" VerticalAlignment="Center"
                         TextTrimming="CharacterEllipsis" />
              <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="2" Classes="row-actions">
                <Button Classes="icon" Click="OnMoveUpClick" Tag="{Binding}"
                        ToolTip.Tip="{DynamicResource L.Panel.Up}" Content="↑" />
                <Button Classes="icon" Click="OnMoveDownClick" Tag="{Binding}"
                        ToolTip.Tip="{DynamicResource L.Panel.Down}" Content="↓" />
              </StackPanel>
            </Grid>
          </RadioButton>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </StackPanel>
</UserControl>
```

`ElementListPanel.axaml.cs`（code-behind，全部经 `Editor.SelectFromListCommand` 与层序命令，不自己改模型）：

```csharp
private void OnRowClick(object? sender, RoutedEventArgs e)
    => ((sender as RadioButton)?.Tag as ElementListItemViewModel)?.Let(item =>
        (DataContext as EditorViewModel)?.SelectFromListCommand.Execute(item));

private void OnMoveUpClick(object? sender, RoutedEventArgs e)
    => ((sender as Button)?.Tag as ElementListItemViewModel)?.Let(item =>
        (DataContext as EditorViewModel)?.SelectFromListCommand.Execute(item) is var _ && true
            ? ((EditorViewModel?)DataContext)!.BringForwardCommand.Execute(null)
            : default);
```

—— 上面的写法先选中再上移；若嫌绕，直接：

```csharp
private void OnMoveUpClick(object? sender, RoutedEventArgs e)
{
    if (DataContext is not EditorViewModel editor) return;
    if ((sender as Button)?.Tag is not ElementListItemViewModel item) return;
    editor.Selection = item.Annotation;
    editor.BringForwardCommand.Execute(null);
}
private void OnMoveDownClick(object? sender, RoutedEventArgs e)
{
    if (DataContext is not EditorViewModel editor) return;
    if ((sender as Button)?.Tag is not ElementListItemViewModel item) return;
    editor.Selection = item.Annotation;
    editor.SendBackwardCommand.Execute(null);
}
```

行选中高亮滚动可见：code-behind 订阅 `Editor.PropertyChanged`（`Selection`）与列表 `CollectionChanged`，用 `ItemsControl.ItemContainerGenerator` 找到选中项容器调 `BringIntoView()`（guard：容器未 realize 时跳过）。整栏显隐绑定 `Editor.HasElements`（XAML `IsVisible="{Binding HasElements}"`，挂 UserControl 根或由 MainWindow 控）。

`IconKeyToGeometryConverter.cs`：

```csharp
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Novashot.App.Converters;

/// <summary>"Icon.Rect" 等资源 key → StreamGeometry（图标几何在 App 资源字典，静态不随主题变）。</summary>
public sealed class IconKeyToGeometryConverter : IValueConverter
{
    public static readonly IconKeyToGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key
           && Application.Current?.TryGetResource(key, ThemeVariant.Default, out var geo) == true
           && geo is StreamGeometry g
            ? g
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
```

**步骤 5：`MainWindow.axaml` 三列**

`:201` 的 `<Grid IsVisible="{Binding IsInEditorMode}" ColumnDefinitions="*,264">` 改为：

```xml
<Grid IsVisible="{Binding IsInEditorMode}" ColumnDefinitions="Auto,*,264">
  <!-- 左：元素列表 -->
  <controls:ElementListPanel Grid.Column="0" x:Name="ElementList"
                             DataContext="{Binding Editor}" VerticalAlignment="Stretch" />
  <!-- 中：画布（原 Grid.Column="0" 改 1） -->
  …
  <!-- 右：原 Grid.Column="1" 改 2 -->
```

窗口 `UserControl.Resources`/`Window.Resources` 注册 `<conv:IconKeyToGeometryConverter x:Key="IconKeyToGeometry" />`（ElementListPanel 若独立成 UserControl，转换器注册在它自己的 `UserControl.Resources`）。

**步骤 6：`StylePanel.axaml` 分组化**

按设计改结构（现有颜色/描边宽/字号控件原样搬进对应组；组标题用 `Classes="label"`）：

```
Header：SelectionKindLabel（既有）
组 公共（恒显）：Colour（既有）+ Stroke（既有）
组 几何（IsVisible="{Binding IsGeometryVisible}"）：
    4×NumericUpDown（Minimum 0 / Maximum 10000，FormatString="0"，SpinButtonAlignment="Left"）
    X→SelX、Y→SelY、Width→SelW、Height→SelH
    Width/Height 行 IsEnabled="{Binding IsSizeEditable}"，
    ToolTip.Tip="{DynamicResource L.Panel.GeometryReadonlyHint}"（仅当不可编辑时有意义）
组 效果（IsVisible="{Binding IsEffectVisible}"）：
    Slider Minimum="{Binding EffectMin}" Maximum="{Binding EffectMax}" Value="{Binding EffectStrength}"
    标签 Text 按类型：Mosaic→L.Panel.BlockSize、Blur→L.Panel.Strength
    （Mosaic 时标签切换可用 StylePanelViewModel 加 string 属性 EffectLabel，
      _ => T("Panel.Strength")，Mosaic => T("Panel.BlockSize")）
组 层序（IsVisible="{Binding HasSelection}"）：
    4 Button：Command="{Binding Editor.BringForwardCommand}"（Content 用 ↑/↓/⤒/⤓ 或文字，Tip 用新 key）
```

`FontSize` 既有组保留在效果组内或独立，行为不变。

**步骤 7：运行验证通过**

运行：`dotnet test tests/Novashot.App.Tests -f net10.0-windows10.0.19041.0 --filter ElementListPanelXamlTests` → PASS
运行：`dotnet test tests/Novashot.App.Tests -f net10.0 --filter LocKeyConsistencyTests` → PASS（双表同步）

---

### 任务 8：收口 — 全量构建、回归、快照目检

**步骤 1：全量构建（警告即错误）**

运行：`dotnet build Novashot.slnx`
预期：0 警告 0 错误。

**步骤 2：全量测试**

运行：
- `dotnet test tests/Novashot.Core.Tests`
- `dotnet test tests/Novashot.App.Tests -f net10.0`
- `dotnet test tests/Novashot.App.Tests -f net10.0-windows10.0.19041.0`

预期：全绿。

**步骤 3：视觉快照**

运行：`NOVASHOT_SNAPSHOT=1 dotnet test tests/Novashot.App.Tests -f net10.0-windows10.0.19041.0 --filter UiSnapshot`
预期：`artifacts/ui/` 出快照；人工目检三栏布局、左列表行、Inspector 分组、淡选中框。
（若快照场景没覆盖新 UI，给 `UiSnapshotTests` 补一个「添加两个标注 → 三栏编辑器」场景，作为本任务的子步骤。）

**步骤 4：手动验证清单**（`dotnet run --project src/Novashot.App` 后走一遍）

1. 画箭头 → 不切工具，右侧立即显示箭头属性；画布上有淡虚线框、无手柄
2. 按 V → 手柄出现，可拖动/缩放/Delete
3. 点空白 → 取消选中，面板回到默认样式态
4. 左列表出现两行（栈顶在上）；点第一行 → 选中 + 自动切 V
5. 行内 ↑↓ → 层序变化且 Ctrl+Z 可撤销；列表顺序同步
6. 选 Mosaic → 效果组滑条可调颗粒度；Ctrl+滚轮行为不变
7. 选 Rectangle → 改 X/Y/宽/高 数值 → 画布即时变化；Ctrl+Z 逐步回退
8. 删光所有标注 → 左栏整栏消失

---

## 风险与回滚

- 最大回归面 = 删掉「切工具清空 Selection」：任务 3 步骤 5 全量回归 + 任务 5 条件化渲染必须同批落地（顺序：3 → 5 之间画布会短暂出现「切工具后手柄仍在」的中间态，本计划按序执行风险可控，两任务都完成前不要单独提交验证视觉）。
- `ReorderAnnotationCommand` 的索引漂移限制与 `RemoveAnnotationCommand` 同款；Undo 栈内混用层序/删除命令的极端序列可能出现次序偏差，接受（与既有命令族一致）。
- NumericUpDown 双向绑定浮点精度：`SelX` setter 里已有 `delta==0` 幂等短路，避免无意义入栈。

---

## 审查范围

详见：`docs/plans/2026-11-23-inspector-element-list-review-scope.md`
