using CommunityToolkit.Mvvm.ComponentModel;
using Prtsc.Core.Capture;

namespace Prtsc.App.ViewModels;

public enum MainViewMode
{
    CaptureIdle,
    Editor,
}

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private MainViewMode _mode = MainViewMode.CaptureIdle;

    [ObservableProperty]
    private string _statusText = "Ready. Press hotkey or use toolbar to capture.";

    /// <summary>
    /// 当前打开的编辑器（M4 起）。为 null 表示尚未进入编辑器。
    /// 生命期由主 VM 拥有；<see cref="EnterEditor"/> 会释放旧实例。
    /// </summary>
    [ObservableProperty]
    private EditorViewModel? _editor;

    public bool IsInCaptureMode => Mode == MainViewMode.CaptureIdle;
    public bool IsInEditorMode => Mode == MainViewMode.Editor;

    partial void OnModeChanged(MainViewMode value)
    {
        OnPropertyChanged(nameof(IsInCaptureMode));
        OnPropertyChanged(nameof(IsInEditorMode));
    }

    public void EnterEditor(CapturedImage image)
    {
        Editor?.Dispose();
        Editor = new EditorViewModel(image);
        Mode = MainViewMode.Editor;
        StatusText = $"Captured {image.Width} x {image.Height} @ {image.ScaleFactor:0.##}x";
    }

    public void ReturnToCapture()
    {
        Editor?.Dispose();
        Editor = null;
        Mode = MainViewMode.CaptureIdle;
        StatusText = "Ready.";
    }
}
