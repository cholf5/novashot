using Xunit;

namespace Prtsc.App.Tests;

/// <summary>
/// 实例化真实 Avalonia 控件的 headless 测试共用此 collection：
/// xunit 默认按类并行，多个 HeadlessUnitTestSession 并存会争抢 UI 渲染循环导致偶发失败。
/// </summary>
[CollectionDefinition("HeadlessUi", DisableParallelization = true)]
public sealed class HeadlessUiTestCollection;
