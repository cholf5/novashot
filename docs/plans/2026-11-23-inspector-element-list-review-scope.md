# 审查范围

**关联计划**: docs/plans/2026-11-23-inspector-element-list.md
**开始时间**: 2026-11-23（秒级时间戳执行开始时由执行会话补记）

## 预期修改文件

- src/Novashot.Core/Annotation/AnnotationDocument.cs
- src/Novashot.Core/Annotation/History/ReorderAnnotationCommand.cs（新建）
- src/Novashot.Core/Annotation/Editing/InspectorGeometry.cs（新建）
- src/Novashot.App/ViewModels/EditorViewModel.cs
- src/Novashot.App/ViewModels/ElementListItemViewModel.cs（新建）
- src/Novashot.App/ViewModels/StylePanelViewModel.cs
- src/Novashot.App/Controls/SkiaCanvas.cs
- src/Novashot.App/Controls/ElementListPanel.axaml + .axaml.cs（新建）
- src/Novashot.App/Controls/StylePanel.axaml
- src/Novashot.App/Converters/IconKeyToGeometryConverter.cs（新建）
- src/Novashot.App/Views/MainWindow.axaml
- src/Novashot.App/Localization/Loc.cs
- tests/Novashot.Core.Tests/AnnotationDocumentMoveTests.cs（新建）
- tests/Novashot.Core.Tests/InspectorGeometryTests.cs（新建）
- tests/Novashot.App.Tests/EditorSelectionRulesTests.cs（新建）
- tests/Novashot.App.Tests/ElementListViewModelTests.cs（新建）
- tests/Novashot.App.Tests/StylePanelInspectorTests.cs（新建）
- tests/Novashot.App.Tests/ElementListPanelXamlTests.cs（新建）

## 设计文档

- docs/plans/2026-11-23-inspector-element-list-design.md
