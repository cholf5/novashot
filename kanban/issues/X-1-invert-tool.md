# X-1 反色工具

- **优先级**: P3 ｜ **复杂度**: S ｜ **状态**: Done（功能随 GAP-1 于 2026-11-01 交付；本卡 2026-11-10 对账时补关）｜ **来源**: flameshot `invert` 工具
- **关联**: `PixelEffectAnnotation` / `RegionEffectTool` 基建现成（Mosaic/Blur 同族）

## 目标

flameshot 的小工具，选区内反色。基建齐全，实现成本低，随缘领取。

## 验收标准

- [x] 区域反色效果可撤销、可导出——`InvertAnnotation : PixelEffectAnnotation` 走既有区域效果渲染/撤销链路。
- [x] 热键挂 i（`MainWindow.axaml.cs` `Key.I => ToolKind.Invert`，与现有工具无冲突）。

## 测试用例

- [x] 反色像素语义单测（`tests/Novashot.Core.Tests/InvertAnnotationTests.cs`：区域内外像素语义 + 退化区域）。

## 落点

- `src/Novashot.Core/Annotation/Model/InvertAnnotation.cs`（先把选区内源像素画回原位再反色，区域外像素零改动）
- `src/Novashot.App/ViewModels/EditorViewModel.cs`（`RegionEffectTool` 工厂）、`MainWindow.axaml`（工具坞按钮 + 图标）、`Loc.cs`（`Tool.Invert` / `Kind.Invert` 双语）

## Verification

- How to run/verify: `dotnet test tests/Novashot.Core.Tests --filter InvertAnnotationTests`
- Results: Core 全量绿（并入 GAP-1 的 553/553 基线之后历次全量跑通过）
- 真机项：QA-1 §D（工具坞 15 工具各画一笔）
