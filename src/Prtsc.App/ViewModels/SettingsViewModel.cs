using CommunityToolkit.Mvvm.ComponentModel;
using Prtsc.App.Localization;
using Prtsc.Core.Autostart;
using Prtsc.Core.Config;
using Prtsc.Core.Hotkeys;

namespace Prtsc.App.ViewModels;

/// <summary>
/// S-1 设置中心 VM：整窗编辑草稿副本，点「保存」才原子生效（云设置窗口同一套心智）。
/// 副作用分两层：热键重注册与语言全局刷新由 App 订阅 <see cref="IConfigStore.Changed"/> 统一处理；
/// 自启开关在此直调 <see cref="IAutostartService"/>（变更才调，失败中断保存）。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public static readonly string[] OutputRatios = { "auto", "1:1", "4:3", "3:2", "16:9", "5:3", "9:16", "3:4", "2:3" };
    private static readonly string[] Patterns = { "timestamp", "date", "increment" };
    private static readonly string[] Languages = { "system", "en", "zh-CN" };
    private static readonly string[] BorderTypes = { "outside", "center", "inside" };
    private static readonly string[] Formats = { "png", "jpeg" };

    private readonly IConfigStore _store;
    private readonly IAutostartService _autostart;

    // —— 热键 ——
    [ObservableProperty] private string _hotkeyFullscreen = "";
    [ObservableProperty] private string _hotkeyRegion = "";
    [ObservableProperty] private string _hotkeyWindow = "";

    // —— 常规 ——
    [ObservableProperty] private bool _launchOnStartup;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _showNotification;
    [ObservableProperty] private bool _checkOnStartup;
    [ObservableProperty] private int _languageIndex;

    // —— QuickSave ——
    [ObservableProperty] private string _quickSaveFolder = "";
    [ObservableProperty] private int _quickSavePatternIndex;

    // —— 导出 ——
    [ObservableProperty] private int _exportFormatIndex;
    [ObservableProperty] private int _jpegQuality = 95;
    [ObservableProperty] private bool _exportIncludeBackground = true;
    [ObservableProperty] private bool _autoCopyToClipboard = true;

    /// <summary>JPEG 质量滑杆仅在格式选 jpeg 时可见。</summary>
    public bool IsJpegQualityVisible => ExportFormatIndex == 1;

    partial void OnExportFormatIndexChanged(int value) => OnPropertyChanged(nameof(IsJpegQualityVisible));

    // —— 捕获 ——
    [ObservableProperty] private bool _includeCursor = true;

    // —— 编辑器默认值 ——
    [ObservableProperty] private int _editorPadding = 40;
    [ObservableProperty] private int _editorCornerRadius = 12;
    [ObservableProperty] private int _editorShadowSize = 20;
    [ObservableProperty] private int _editorOutputRatioIndex;
    [ObservableProperty] private bool _editorShowBackground = true;
    [ObservableProperty] private string _editorBackgroundColor = "";
    [ObservableProperty] private int _editorInset;
    [ObservableProperty] private bool _editorAutoBackground = true;
    [ObservableProperty] private string _editorInsetBackgroundColor = "";
    [ObservableProperty] private int _editorShapeCornerRadius;
    [ObservableProperty] private bool _borderEnabled;
    [ObservableProperty] private int _borderWeight = 2;
    [ObservableProperty] private string _borderColor = "";
    [ObservableProperty] private int _borderOpacity = 100;
    [ObservableProperty] private int _borderTypeIndex = 1;

    [ObservableProperty] private string _saveHint = "";

    public SettingsViewModel(IConfigStore store)
        : this(store, AppServices.GetRequiredService<IAutostartService>()) { }

    public SettingsViewModel(IConfigStore store, IAutostartService autostart)
    {
        _store = store;
        _autostart = autostart;
        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        var c = _store.Current;
        HotkeyFullscreen = c.Hotkeys.Fullscreen;
        HotkeyRegion = c.Hotkeys.Region;
        HotkeyWindow = c.Hotkeys.Window;
        LaunchOnStartup = c.Startup.LaunchOnStartup;
        MinimizeToTray = c.Startup.MinimizeToTray;
        CloseToTray = c.Startup.CloseToTray;
        ShowNotification = c.Startup.ShowNotification;
        CheckOnStartup = c.Update.CheckOnStartup;
        LanguageIndex = MapTo(Languages, c.Language);
        QuickSaveFolder = c.QuickSave.Folder;
        QuickSavePatternIndex = MapTo(Patterns, c.QuickSave.Pattern);
        ExportFormatIndex = MapTo(Formats, c.Export.DefaultFormat);
        JpegQuality = c.Export.JpegQuality;
        ExportIncludeBackground = c.Export.IncludeBackground;
        AutoCopyToClipboard = c.Export.AutoCopyToClipboard;
        IncludeCursor = c.Capture.IncludeCursor;
        EditorPadding = c.Editor.Padding;
        EditorCornerRadius = c.Editor.CornerRadius;
        EditorShadowSize = c.Editor.ShadowSize;
        EditorOutputRatioIndex = MapTo(OutputRatios, c.Editor.OutputRatio);
        EditorShowBackground = c.Editor.ShowBackground;
        EditorBackgroundColor = c.Editor.BackgroundColor;
        EditorInset = c.Editor.Inset;
        EditorAutoBackground = c.Editor.AutoBackground;
        EditorInsetBackgroundColor = c.Editor.InsetBackgroundColor;
        EditorShapeCornerRadius = c.Editor.ShapeCornerRadius;
        BorderEnabled = c.Editor.BorderEnabled;
        BorderWeight = c.Editor.BorderWeight;
        BorderColor = c.Editor.BorderColor;
        BorderOpacity = c.Editor.BorderOpacity;
        BorderTypeIndex = MapTo(BorderTypes, c.Editor.BorderType);
    }

    /// <summary>校验 → 自启 → 一次 Update 写全部字段（Changed 触发 App 层热键/语言副作用）。失败时 config 不动。</summary>
    public bool Save()
    {
        if (!ValidateHotkeys())
        {
            SaveHint = Loc.T("Settings.Hint.HotkeyInvalid");
            return false;
        }

        try
        {
            if (LaunchOnStartup && !_autostart.IsEnabled) _autostart.Enable();
            else if (!LaunchOnStartup && _autostart.IsEnabled) _autostart.Disable();
        }
        catch (Exception ex)
        {
            SaveHint = Loc.T("Settings.Hint.AutostartFailed", ex.Message);
            return false;
        }

        _store.Update(c =>
        {
            c.Hotkeys.Fullscreen = HotkeyFullscreen.Trim();
            c.Hotkeys.Region = HotkeyRegion.Trim();
            c.Hotkeys.Window = HotkeyWindow.Trim();
            c.Startup.LaunchOnStartup = LaunchOnStartup;
            c.Startup.MinimizeToTray = MinimizeToTray;
            c.Startup.CloseToTray = CloseToTray;
            c.Startup.ShowNotification = ShowNotification;
            c.Update.CheckOnStartup = CheckOnStartup;
            c.Language = Languages[Math.Clamp(LanguageIndex, 0, Languages.Length - 1)];
            c.QuickSave.Folder = QuickSaveFolder.Trim();
            c.QuickSave.Pattern = Patterns[Math.Clamp(QuickSavePatternIndex, 0, Patterns.Length - 1)];
            c.Export.DefaultFormat = Formats[Math.Clamp(ExportFormatIndex, 0, Formats.Length - 1)];
            c.Export.JpegQuality = JpegQuality;
            c.Export.IncludeBackground = ExportIncludeBackground;
            c.Export.AutoCopyToClipboard = AutoCopyToClipboard;
            c.Capture.IncludeCursor = IncludeCursor;
            c.Editor.Padding = EditorPadding;
            c.Editor.CornerRadius = EditorCornerRadius;
            c.Editor.ShadowSize = EditorShadowSize;
            c.Editor.OutputRatio = OutputRatios[Math.Clamp(EditorOutputRatioIndex, 0, OutputRatios.Length - 1)];
            c.Editor.ShowBackground = EditorShowBackground;
            c.Editor.BackgroundColor = EditorBackgroundColor.Trim();
            c.Editor.Inset = EditorInset;
            c.Editor.AutoBackground = EditorAutoBackground;
            c.Editor.InsetBackgroundColor = EditorInsetBackgroundColor.Trim();
            c.Editor.ShapeCornerRadius = EditorShapeCornerRadius;
            c.Editor.BorderEnabled = BorderEnabled;
            c.Editor.BorderWeight = BorderWeight;
            c.Editor.BorderColor = BorderColor.Trim();
            c.Editor.BorderOpacity = BorderOpacity;
            c.Editor.BorderType = BorderTypes[Math.Clamp(BorderTypeIndex, 0, BorderTypes.Length - 1)];
        });
        _ = _store.SaveAsync();
        SaveHint = Loc.T("Settings.Hint.Saved");
        return true;
    }

    /// <summary>三条热键各自可解析且 (modifiers, vk) 互异。</summary>
    private bool ValidateHotkeys()
    {
        var parsed = new List<(uint mods, uint vk)>(3);
        foreach (var spec in new[] { HotkeyFullscreen, HotkeyRegion, HotkeyWindow })
        {
            if (string.IsNullOrWhiteSpace(spec) || !HotkeyParser.TryParse(spec, out var mods, out var vk))
                return false;
            if (parsed.Contains((mods, vk)))
                return false;
            parsed.Add((mods, vk));
        }

        return true;
    }

    private static int MapTo(string[] table, string value)
    {
        var i = Array.IndexOf(table, value);
        return i < 0 ? 0 : i;
    }
}
