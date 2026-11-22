namespace Novashot.Core.Annotation.History;

/// <summary>
/// 命令模式的可撤销操作。<see cref="Do"/> 与 <see cref="Undo"/> 必须是幂等对：
/// 先 Do 再 Undo 后状态回到 Do 前；反复 Do / Undo 都不应抛异常。
/// </summary>
public interface IUndoableCommand
{
    /// <summary>供 UI 展示（状态栏 / 撤销菜单提示）。</summary>
    string Description { get; }

    void Do();

    void Undo();
}
