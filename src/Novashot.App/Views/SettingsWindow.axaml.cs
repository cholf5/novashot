using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Novashot.App.Hotkeys;
using Novashot.App.Localization;
using Novashot.App.ViewModels;
using Novashot.Core.Autostart;
using Novashot.Core.Config;

namespace Novashot.App.Views;

/// <summary>
/// S-1 设置中心：左栏分段导航 + 六分区（常规/热键/QuickSave/导出/编辑器默认值/捕获）。
/// 整窗草稿 + 保存/取消（云设置窗口同一套心智）；热键录制在 OnKeyDown 捕获，
/// 保存后的热键重注册与语言全局刷新由 App 订阅 store.Changed 统一处理。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private Button? _recordingButton;
    private Action<string>? _recordingSetter;

    public SettingsWindow() : this(AppServices.GetRequiredService<IConfigStore>()) { }

    public SettingsWindow(IConfigStore store)
        : this(store, AppServices.GetRequiredService<IAutostartService>()) { }

    public SettingsWindow(IConfigStore store, IAutostartService autostart)
    {
        _vm = new SettingsViewModel(store, autostart);
        DataContext = _vm;
        InitializeComponent();
        ApplyLanguageRadio();
    }

    // —— 分段导航（OnPanelTabClick 模式泛化到 6 节） ——

    private void OnNavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag }) return;
        ShowSection(tag);
    }

    private void ShowSection(string tag)
    {
        foreach (var (buttonName, hostName, key) in new[]
                 {
                     ("NavGeneral", "GeneralHost", "general"),
                     ("NavHotkeys", "HotkeysHost", "hotkeys"),
                     ("NavQuickSave", "QuickSaveHost", "quicksave"),
                     ("NavExport", "ExportHost", "export"),
                     ("NavEditor", "EditorHost", "editor"),
                     ("NavCapture", "CaptureHost", "capture"),
                 })
        {
            if (this.FindControl<ToggleButton>(buttonName) is { } button) button.IsChecked = key == tag;
            if (this.FindControl<ScrollViewer>(hostName) is { } host) host.IsVisible = key == tag;
        }
    }

    // —— 语言分段（Tag = LanguageIndex） ——

    private void OnLangClick(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, out var index))
            _vm.LanguageIndex = index;
    }

    private void ApplyLanguageRadio()
    {
        var idx = Math.Clamp(_vm.LanguageIndex, 0, 2);
        if (this.FindControl<RadioButton>("LangSystem") is { } system) system.IsChecked = idx == 0;
        if (this.FindControl<RadioButton>("LangEn") is { } en) en.IsChecked = idx == 1;
        if (this.FindControl<RadioButton>("LangZh") is { } zh) zh.IsChecked = idx == 2;
    }

    // —— 热键录制：点击进入录制态，下一个组合键写入对应草稿字段，Esc 退出 ——

    private void OnRecordClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Name: var name }) return;
        var setter = name switch
        {
            "RecordFullscreen" => (Action<string>)(v => _vm.HotkeyFullscreen = v),
            "RecordRegion" => v => _vm.HotkeyRegion = v,
            "RecordWindow" => v => _vm.HotkeyWindow = v,
            _ => throw new InvalidOperationException($"Unknown hotkey record target: {name}"),
        };

        EndRecording();
        _recordingButton = sender as Button;
        _recordingSetter = setter;
        _recordingButton!.Content = Loc.T("Settings.Hotkeys.Recording");
    }

    private void EndRecording()
    {
        if (_recordingButton is { } button) button.ClearValue(ContentProperty);
        _recordingButton = null;
        _recordingSetter = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_recordingSetter is null || _recordingButton is null) return;

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            EndRecording();
            return;
        }

        if (KeyCombo.TryToSpec(e.Key, e.KeyModifiers, out var spec))
        {
            _recordingSetter(spec);
            EndRecording();
        }
    }

    // —— 预设色块（背景，Tag = hex）；描边色仅 TextBox，不给预设 ——

    private void OnBgPresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string hex }) _vm.EditorBackgroundColor = hex;
    }

    // —— QuickSave 文件夹 ——

    private async void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
        });
        if (folders.Count > 0) _vm.QuickSaveFolder = folders[0].Path.LocalPath;
    }

    // —— 保存 / 取消 ——

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.Save())
            Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
