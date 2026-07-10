using System;
using Avalonia.Controls;

namespace Prtsc.App.Controls;

/// <summary>
/// 选框旁边的悬浮工具环。M3-1 版本：Cancel / Confirm 两键。
/// 后续（M4+）在同一 ring 上添加 Copy / Save / Pin / 编辑器工具入口。
/// </summary>
public partial class FloatingToolRing : UserControl
{
    public event EventHandler? CancelRequested;
    public event EventHandler? ConfirmRequested;

    public FloatingToolRing()
    {
        InitializeComponent();

        this.FindControl<Button>("CancelButton")!.Click +=
            (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
        this.FindControl<Button>("ConfirmButton")!.Click +=
            (_, _) => ConfirmRequested?.Invoke(this, EventArgs.Empty);
    }
}
