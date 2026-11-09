using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace Prtsc.App.Localization;

/// <summary>
/// 极简双语文案层（简体中文 / English）。
///
/// 用法：
/// <list type="bullet">
/// <item>XAML：<c>Text="{DynamicResource L.Capture.Title}"</c></item>
/// <item>代码：<c>Loc.T("Capture.Title")</c></item>
/// </list>
///
/// 切语言 = 把整张表按 <c>L.</c> 前缀重新写进 <see cref="Application.Resources"/>；
/// DynamicResource 会自己刷新，窗口不用重建。
/// </summary>
public static class Loc
{
    public const string SystemDefault = "system";
    public const string English = "en";
    public const string ChineseSimplified = "zh-CN";

    /// <summary>用户偏好（config 里的原值，可能为 system）。</summary>
    public static string Preference { get; private set; } = SystemDefault;

    /// <summary>实际生效的语言（en / zh-CN）。</summary>
    public static string Current { get; private set; } = English;

    /// <summary>语言变更事件（托盘菜单勾选状态等视图侧刷新用）。</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>系统语言 → 我们支持的两个之一。非中文一律英文。</summary>
    public static string Resolve(string? preference)
    {
        var pref = string.IsNullOrWhiteSpace(preference) ? SystemDefault : preference!;
        if (pref.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return ChineseSimplified;
        if (pref.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return English;
        // system：跟随 OS
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? ChineseSimplified
            : English;
    }

    /// <summary>取文案；找不到就退回英文，再找不到返回 key（开发期一眼看出漏翻）。</summary>
    public static string T(string key)
    {
        var table = Current == ChineseSimplified ? Strings.Zh : Strings.En;
        if (table.TryGetValue(key, out var value)) return value;
        return Strings.En.TryGetValue(key, out var fallback) ? fallback : key;
    }

    /// <summary>带参数的文案（string.Format）。</summary>
    public static string T(string key, params object?[] args) => string.Format(T(key), args);

    /// <summary>启动时调用：解析偏好并写入资源字典。</summary>
    public static void Initialize(string? preference, Application app)
    {
        Preference = string.IsNullOrWhiteSpace(preference) ? SystemDefault : preference!;
        Current = Resolve(Preference);
        Apply(app);
    }

    /// <summary>运行时切语言（托盘菜单 / 设置页）；顺带刷新窗口标题。</summary>
    public static void SetPreference(string? preference, Application app)
    {
        Preference = string.IsNullOrWhiteSpace(preference) ? SystemDefault : preference!;
        var next = Resolve(Preference);
        if (next != Current)
        {
            Current = next;
            Apply(app);
        }
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// S-1：把 config 持久化的语言偏好应用为当前语言（设置保存 → store.Changed → 这里）。
    /// 只接受三个合法值，且与当前不同才切换；托盘手改的旧链路继续走 SetPreference。
    /// </summary>
    public static void ApplyStoredPreference(string? stored, Application app)
    {
        if (stored is not (SystemDefault or English or ChineseSimplified)) return;
        if (string.Equals(Preference, stored, StringComparison.Ordinal)) return;
        SetPreference(stored, app);
    }

    /// <summary>把整张表写进 Application.Resources（key 带 L. 前缀）。</summary>
    public static void Apply(Application app)
    {
        var table = Current == ChineseSimplified ? Strings.Zh : Strings.En;

        foreach (var key in Strings.En.Keys)
        {
            app.Resources["L." + key] = T(key);
        }

        // 表里可能有英文没写、中文补了的键，一并覆盖
        foreach (var key in table.Keys)
        {
            app.Resources["L." + key] = T(key);
        }
    }
}

/// <summary>文案表。key 不带 L. 前缀（Loc 统一加）。</summary>
internal static class Strings
{
    public static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        // ---- common ----
        ["App.Title"] = "Prtsc",
        ["Overlay.Hint"] = "Drag to select · drag handles to adjust · Enter to confirm · Esc to cancel",
        ["Overlay.PickerHint"] = "Pick mode: click to sample a colour · C to exit",
        ["Overlay.PickerCopied"] = "Copied {0}",
        ["Pin.Hint"] = "Drag to move · scroll to zoom · double-click or Esc to close",
        ["Ring.Cancel"] = "Cancel (Esc)",
        ["Ring.Confirm"] = "Confirm (Enter)",

        ["Common.Save"] = "Save",
        ["Common.Cancel"] = "Cancel",
        ["Common.Close"] = "Close",
        ["Common.Refresh"] = "Refresh",
        ["Common.Delete"] = "Delete",
        ["Common.Copy"] = "Copy",
        ["Common.Open"] = "Open",
        ["Common.None"] = "None",
        ["Common.Solid"] = "Solid",
        ["Common.Gradient"] = "Gradient",
        ["Common.Auto"] = "Auto",

        // ---- capture / idle ----
        ["Capture.Title"] = "Capture",
        ["Capture.Lede"] = "Pick a mode, or use a global hotkey from anywhere.",
        ["Capture.Fullscreen"] = "Fullscreen",
        ["Capture.FullscreenHint"] = "Whole virtual desktop, all displays",
        ["Capture.Region"] = "Region",
        ["Capture.RegionHint"] = "Drag a box, then annotate",
        ["Capture.Window"] = "Window",
        ["Capture.WindowHint"] = "Pick a window from a live list",
        ["Capture.Paste"] = "Paste an image",
        ["Capture.PasteHint"] = "Open whatever is on the clipboard",
        ["Capture.Recent"] = "Recent",
        ["Capture.OpenLibrary"] = "Open library",
        ["Capture.RecentEmpty"] = "Your shots show up here.",
        ["Capture.Ready"] = "Ready",
        ["Capture.RegionOn"] = "Region capture on",
        ["Capture.HideToTray"] = "Hide to tray",
        ["Capture.Settings"] = "Settings",
        ["Capture.TrayHint"] = "Right-click the tray icon for settings",
        ["Capture.Language"] = "Language",
        ["Capture.LanguageSystem"] = "Follow system",

        // ---- editor ----
        ["Editor.Title"] = "Editor",
        ["Editor.Untitled"] = "Untitled capture",
        ["Editor.Back"] = "Back to capture",
        ["Editor.Undo"] = "Undo",
        ["Editor.Redo"] = "Redo",
        ["Editor.Save"] = "Save",
        ["Editor.SaveTip"] = "Export as PNG / JPEG / WebP (format follows the file extension)",
        ["Editor.ExportDialogTitle"] = "Export image",
        ["Editor.ExportTypePng"] = "PNG image",
        ["Editor.ExportTypeJpeg"] = "JPEG image",
        ["Editor.ExportTypeWebp"] = "WebP image",
        ["Editor.Copy"] = "Copy",
        ["Editor.CopyTip"] = "Copy the composed image to the clipboard",
        ["Editor.More"] = "More actions",
        ["Editor.QuickSave"] = "Quick save",
        ["Editor.Upload"] = "Upload to cloud",
        ["Editor.Library"] = "Library",
        ["Editor.Pin"] = "Pin to screen",
        ["Editor.CloudSettings"] = "Cloud settings…",
        ["Editor.Settings"] = "Settings…",
        ["Editor.Delete"] = "Delete selection",
        ["Editor.StatusHint"] = "Esc to cancel · Enter to copy",
        ["Editor.CanvasHint"] = "Drag on the canvas to draw. Ctrl + wheel changes stroke width for the tool in hand.",

        // ---- tools ----
        ["Tool.Select"] = "Select / move / resize (V)",
        ["Tool.Rectangle"] = "Rectangle (R)",
        ["Tool.Ellipse"] = "Ellipse (E)",
        ["Tool.Line"] = "Line (L)",
        ["Tool.ArrowStraight"] = "Arrow (A)",
        ["Tool.ArrowCurved"] = "Curved arrow (O)",
        ["Tool.Text"] = "Text (T)",
        ["Tool.Number"] = "Number counter (N)",
        ["Tool.Spotlight"] = "Spotlight (S)",
        ["Tool.Pencil"] = "Pencil (P)",
        ["Tool.Highlighter"] = "Highlighter (H)",
        ["Tool.Mosaic"] = "Pixelate region (M)",
        ["Tool.Blur"] = "Blur region (B)",
        ["Tool.Invert"] = "Invert colors (I)",
        ["Tool.Crop"] = "Crop (X) — non-destructive",

        // ---- panel ----
        ["Kind.None"] = "Nothing selected",
        ["Kind.Rectangle"] = "Rectangle",
        ["Kind.Ellipse"] = "Ellipse",
        ["Kind.Line"] = "Line",
        ["Kind.ArrowStraight"] = "Arrow",
        ["Kind.ArrowCurved"] = "Curved arrow",
        ["Kind.Text"] = "Text",
        ["Kind.Number"] = "Number",
        ["Kind.Spotlight"] = "Spotlight",
        ["Kind.Pencil"] = "Pencil",
        ["Kind.Highlighter"] = "Highlighter",
        ["Kind.Mosaic"] = "Mosaic",
        ["Kind.Blur"] = "Blur",
        ["Kind.Invert"] = "Invert",

        ["Panel.Style"] = "Style",
        ["Panel.Canvas"] = "Canvas",
        ["Panel.Colour"] = "Colour",
        ["Panel.History"] = "History",
        ["Panel.Stroke"] = "Stroke",
        ["Panel.TextSize"] = "Text size",
        ["Panel.Selection"] = "Selection",
        ["Panel.Opacity"] = "Opacity",
        ["Panel.Position"] = "Position",
        ["Panel.Background"] = "Background",
        ["Panel.BackgroundAuto"] = "Auto (edge colour)",
        ["Panel.BackgroundAutoTip"] = "Use the dominant colour along the image edges",
        ["Panel.BackgroundNoneTip"] = "Transparent: the checkered areas on the canvas stay transparent in the saved image (set padding to 0 to drop the transparent border)",
        ["Panel.Frame"] = "Frame",
        ["Panel.Padding"] = "Padding",
        ["Panel.Radius"] = "Corner radius",
        ["Panel.Shadow"] = "Shadow",
        ["Panel.Border"] = "Border",
        ["Panel.Inset"] = "Inset",
        ["Panel.Outside"] = "Outside",
        ["Panel.Center"] = "Center",
        ["Panel.Inside"] = "Inside",
        ["Panel.CropRatio"] = "Crop ratio",
        ["Panel.NoSelection"] = "Nothing selected",

        // ---- library ----
        ["Library.NoLink"] = "That record has no upload link",
        ["Library.LinkCopied"] = "Link copied",
        ["Library.FileGone"] = "The local file is gone and there is no link",
        ["Library.NoLocalFile"] = "That record has no local file",
        ["Library.PathCopied"] = "Path copied",

        ["Library.Title"] = "Library",
        ["Library.Search"] = "Search filenames",
        ["Library.Empty"] = "No shots yet. Press PrtSc and they land here.",
        ["Library.CopyLink"] = "Copy link",
        ["Library.CopyPath"] = "Copy path",
        ["Library.OpenImage"] = "Open image",
        ["Library.Delete"] = "Delete",
        ["Library.LocalOnly"] = "Local only",
        ["Library.Uploaded"] = "{0} · uploaded",
        ["Library.CountOne"] = "1 shot",
        ["Library.CountMany"] = "{0} shots",
        ["Library.DeleteConfirmTitle"] = "Confirm delete",
        ["Library.DeleteConfirmRecord"] = "Delete \"{0}\"?",
        ["Library.DeleteFileStays"] = "The image file stays on disk.",
        ["Library.DeleteRecordOnly"] = "Only the library record is removed.",

        // ---- cloud settings ----
        ["Cloud.NoneTitle"] = "Nothing is uploaded",
        ["Cloud.NoneHint"] = "Shots stay on this machine. Pick a provider above to upload and copy a link.",
        ["Cloud.Testing"] = "Testing the connection…",
        ["Cloud.TestError"] = "Connection test failed: {0}",
        ["Cloud.PickProviderFirst"] = "Pick a provider (R2 or Google Drive) first.",
        ["Cloud.MissingR2"] = "R2 needs Account ID, Bucket, Access Key ID and Secret Access Key.",
        ["Cloud.MissingDrive"] = "Google Drive needs Client ID, Client Secret and Refresh Token.",
        ["Cloud.Saved"] = "Saved.",

        ["Cloud.Title"] = "Cloud upload",
        ["Cloud.Lede"] = "Shots can be uploaded to Cloudflare R2 (S3-compatible) or Google Drive; the link is copied after upload. Credentials are stored in plain text in config.json on this machine and never leave it.",
        ["Cloud.Test"] = "Test connection",
        ["Cloud.TestTip"] = "Send one connection test with the current credentials (R2 = HeadBucket; Drive = token refresh)",
        ["Cloud.Provider"] = "Provider",
        ["Cloud.ProviderNone"] = "Don't upload",
        ["Cloud.AutoUpload"] = "Upload automatically after saving",
        ["Cloud.Save"] = "Save",
        ["Cloud.R2Hint"] = "Create a token in Cloudflare → R2 → Manage API Tokens, and note the Account ID.",
        ["Cloud.AccountId"] = "Account ID",
        ["Cloud.Bucket"] = "Bucket",
        ["Cloud.AccessKey"] = "Access Key ID",
        ["Cloud.SecretKey"] = "Secret Access Key",
        ["Cloud.PublicUrl"] = "Public URL (optional)",
        ["Cloud.PublicUrlHint"] = "Used to build the link, e.g. https://img.example.com",
        ["Cloud.Directory"] = "Upload directory (optional)",
        ["Cloud.GDriveHint"] = "Create an OAuth Desktop client in Google Cloud Console, then exchange an offline refresh_token.",
        ["Cloud.ClientId"] = "Client ID",
        ["Cloud.ClientSecret"] = "Client Secret",
        ["Cloud.RefreshToken"] = "Refresh token",
        ["Cloud.FolderId"] = "Target folder ID (optional)",
        ["Cloud.FolderIdHint"] = "Leave empty for My Drive root",

        // ---- status bar ----
        ["Status.Ready"] = "Ready",
        ["Status.Captured"] = "Captured {0} × {1} @ {2}×",
        ["Status.Copied"] = "Copied {0} × {1} to the clipboard",
        ["Status.CopyBusy"] = "Copy failed: the clipboard is busy",
        ["Status.CopyFailed"] = "Copy failed: {0}",
        ["Status.QuickSaveUnavailable"] = "Quick save needs a config file",
        ["Status.QuickSaved"] = "Saved {0}",
        ["Status.QuickSavedPath"] = "Saved {0} — path copied",
        ["Status.QuickSaveFailed"] = "Quick save failed: {0}",
        ["Status.Exported"] = "Saved {0}",
        ["Status.ExportedCopied"] = "Saved {0} (also copied to clipboard)",
        ["Status.ExportFailedNoPath"] = "Export failed: no local file path.",
        ["Status.ExportFailed"] = "Export failed: {0}",
        ["Status.UploadFailed"] = "Upload failed: {0}",
        ["Status.UploadedUrl"] = "Uploaded {0} — URL copied",
        ["Status.Uploaded"] = "Uploaded {0}",
        ["Status.UploadCanceled"] = "Upload canceled",
        ["Status.ClipboardEmpty"] = "The clipboard has no image",
        ["Status.ImportFailed"] = "Could not open that image: {0}",
        ["Status.FileNotFound"] = "File not found: {0}",
        ["Status.UnsupportedFormat"] = "Unsupported image format: {0}",
        ["Status.Pinned"] = "Pinned {0} × {1}",
        ["Status.PinFailed"] = "Pin failed: {0}",
        ["Status.DelayCanceled"] = "Delayed capture canceled",
        ["Status.RegionFailed"] = "Region capture failed: {0}",
        ["Status.CaptureFailed"] = "Capture failed: {0}",
        ["Status.WindowFailed"] = "Window capture failed: {0}",

        // ---- N-1 desktop notifications（toast 双行：title + message；开关见 Settings.General.ShowNotification） ----
        ["Notify.Exported.Title"] = "Screenshot saved",
        ["Notify.Exported.Message"] = "Saved to {0}",
        ["Notify.QuickSaved.Title"] = "Quick save complete",
        ["Notify.QuickSaved.Message"] = "Saved to {0}",
        ["Notify.Copied.Title"] = "Copied to clipboard",
        ["Notify.Copied.Message"] = "{0} × {1} pixels",
        ["Notify.Uploaded.Title"] = "Upload complete",
        ["Notify.Uploaded.Message"] = "{0}",

        // ---- delayed capture (D-1) ----
        ["Delay.BadgeCancelHint"] = "Press a capture hotkey to cancel",

        // ---- capture launcher (G-26) ----
        ["Launcher.Title"] = "Capture Launcher",
        ["Launcher.Mode"] = "Mode",
        ["Launcher.ModeFullscreen"] = "Fullscreen",
        ["Launcher.ModeRegion"] = "Region",
        ["Launcher.Geometry"] = "Region geometry",
        ["Launcher.GeometryHint"] = "Physical pixels in virtual-desktop coordinates (X/Y may be negative on multi-monitor). Values outside the desktop are clamped back in.",
        ["Launcher.X"] = "X",
        ["Launcher.Y"] = "Y",
        ["Launcher.W"] = "W",
        ["Launcher.H"] = "H",
        ["Launcher.Delay"] = "Delay",
        ["Launcher.Delay0"] = "No delay",
        ["Launcher.Delay3"] = "3 seconds",
        ["Launcher.Delay5"] = "5 seconds",
        ["Launcher.Start"] = "Start capture",
        ["Launcher.Error.Parse"] = "Enter whole numbers in all fields",
        ["Launcher.Error.Size"] = "Width and height must be greater than 0",

        // ---- tray ----
        ["Tray.Show"] = "Show Prtsc",
        ["Tray.CaptureFullscreen"] = "Capture fullscreen",
        ["Tray.CaptureRegion"] = "Capture region",
        ["Tray.CaptureWindow"] = "Capture window",
        ["Tray.DelayedCapture"] = "Delayed capture",
        ["Tray.DelayedCapture.3s"] = "Fullscreen in 3 seconds",
        ["Tray.DelayedCapture.5s"] = "Fullscreen in 5 seconds",
        ["Tray.CaptureLauncher"] = "Capture Launcher…",
        ["Tray.Library"] = "Library…",
        ["Tray.CloudSettings"] = "Cloud settings…",
        ["Tray.Settings"] = "Settings…",
        ["Tray.Quit"] = "Quit",

        // ---- settings center (S-1) ----
        ["Settings.Title"] = "Settings",
        ["Settings.Nav.General"] = "General",
        ["Settings.Nav.Hotkeys"] = "Hotkeys",
        ["Settings.Nav.QuickSave"] = "QuickSave",
        ["Settings.Nav.Export"] = "Export",
        ["Settings.Nav.Editor"] = "Editor defaults",
        ["Settings.Nav.Capture"] = "Capture",
        ["Settings.General.LaunchOnStartup"] = "Launch on startup",
        ["Settings.General.MinimizeToTray"] = "Start minimized to tray",
        ["Settings.General.CloseToTray"] = "Close button hides to tray",
        ["Settings.General.ShowNotification"] = "Show capture notifications",
        ["Settings.General.CheckUpdates"] = "Check for updates on startup",
        ["Settings.General.FeedUrl"] = "Update feed URL",
        ["Settings.General.FeedUrlHint"] = "Velopack feed base URL — releases.win.json must be reachable under this address, e.g. https://github.com/cholf5/prtsc/releases/latest/download/. Empty = never check.",
        ["Settings.General.Language"] = "Language",
        ["Settings.General.LangSystem"] = "System",
        ["Settings.General.LangEn"] = "English",
        ["Settings.General.LangZh"] = "简体中文",
        ["Settings.Hotkeys.Lede"] = "No modifier needed — a single key works. Click a row, then press the combo.",
        ["Settings.Hotkeys.Fullscreen"] = "Fullscreen capture",
        ["Settings.Hotkeys.Region"] = "Region capture",
        ["Settings.Hotkeys.Window"] = "Window capture",
        ["Settings.Hotkeys.Recording"] = "Press keys… Esc to cancel",
        ["Settings.QuickSave.Folder"] = "Save folder",
        ["Settings.QuickSave.FolderHint"] = "Empty = ~/Pictures/Prtsc",
        ["Settings.QuickSave.Browse"] = "Browse…",
        ["Settings.QuickSave.Pattern"] = "File name",
        ["Settings.QuickSave.PatternTimestamp"] = "Timestamp (prtsc-20261102-094213)",
        ["Settings.QuickSave.PatternDate"] = "Date only (prtsc-20261102)",
        ["Settings.QuickSave.PatternIncrement"] = "Incrementing number (prtsc-0042)",
        ["Settings.Export.Format"] = "Default format",
        ["Settings.Export.Png"] = "PNG (lossless)",
        ["Settings.Export.Jpeg"] = "JPEG (smaller)",
        ["Settings.Export.JpegQuality"] = "JPEG quality",
        ["Settings.Export.IncludeBackground"] = "Include background",
        ["Settings.Export.AutoCopy"] = "Copy to clipboard after export",
        ["Settings.Capture.IncludeCursor"] = "Include mouse cursor (Windows)",
        ["Settings.Capture.IncludeCursorHint"] = "Applies to captures on Windows (window and fullscreen); macOS and Linux ignore this.",
        ["Settings.Capture.Delay"] = "Default delay",
        ["Settings.Capture.Delay0"] = "No delay",
        ["Settings.Capture.Delay3"] = "3 seconds",
        ["Settings.Capture.Delay5"] = "5 seconds",
        ["Settings.Capture.DelayHint"] = "Wait before a shot fires — applies to hotkey and tray captures, and to the tray \"delayed capture\" entries. During the countdown, press any capture hotkey to cancel.",
        ["Settings.Editor.Canvas"] = "Canvas",
        ["Settings.Editor.Padding"] = "Padding",
        ["Settings.Editor.CornerRadius"] = "Corner radius",
        ["Settings.Editor.Shadow"] = "Shadow",
        ["Settings.Editor.Ratio"] = "Output ratio",
        ["Settings.Editor.ShowBackground"] = "Show background",
        ["Settings.Editor.BackgroundColor"] = "Background color",
        ["Settings.Editor.Custom"] = "Custom hex",
        ["Settings.Editor.Inset"] = "Inset",
        ["Settings.Editor.AutoBackground"] = "Auto background color",
        ["Settings.Editor.InsetBackgroundColor"] = "Inset background",
        ["Settings.Editor.ShapeCornerRadius"] = "Shape corner radius",
        ["Settings.Editor.Border"] = "Border",
        ["Settings.Editor.BorderEnabled"] = "Enable border",
        ["Settings.Editor.BorderWeight"] = "Weight",
        ["Settings.Editor.BorderColor"] = "Color",
        ["Settings.Editor.BorderOpacity"] = "Opacity",
        ["Settings.Editor.BorderType"] = "Position",
        ["Settings.Editor.BorderOutside"] = "Outside",
        ["Settings.Editor.BorderCenter"] = "Center",
        ["Settings.Editor.BorderInside"] = "Inside",
        ["Settings.Save"] = "Save",
        ["Settings.Cancel"] = "Cancel",
        ["Settings.Hint.Saved"] = "Saved",
        ["Settings.Hint.HotkeyInvalid"] = "Invalid hotkey — pick a main key, optionally with Ctrl/Alt/Shift/Win",
        ["Settings.Hint.HotkeyDuplicate"] = "Two capture actions share the same hotkey",
        ["Settings.Hint.AutostartFailed"] = "Could not update launch-on-startup: {0}",

        // ---- update prompt (C-1/R-1 velopack) ----
        ["Update.Prompt.Title"] = "Update available",
        ["Update.Prompt.Lede"] = "Prtsc {0} is available (current: {1}).",
        ["Update.Prompt.Install"] = "Download and install",
        ["Update.Prompt.Skip"] = "Skip this version",
        ["Update.Prompt.Later"] = "Later",
        ["Update.Prompt.Downloading"] = "Downloading… {0}%",
        ["Update.Prompt.Restarting"] = "Update downloaded — restarting…",
        ["Update.Prompt.DownloadFailed"] = "Update failed: {0}",
        ["Update.Prompt.NoUpdateOnRetry"] = "No update found anymore — the feed changed?",
        ["Status.UpdateCheckFailed"] = "Update check failed: {0}",

        // ---- window picker ----
        ["Picker.Title"] = "Capture a window",
        ["Picker.Lede"] = "Pick the window to capture. Double-click to grab it.",
        ["Picker.Capture"] = "Capture",
        ["Picker.Empty"] = "No capturable windows found.",
        ["Picker.Untitled"] = "(untitled)",
        ["Picker.Minimized"] = "[minimized]",
    };

    public static readonly Dictionary<string, string> Zh = new(StringComparer.Ordinal)
    {
        // ---- common ----
        ["App.Title"] = "Prtsc",
        ["Overlay.Hint"] = "拖动创建选框 · 拖角/边调整 · Enter 确认 · Esc 取消",
        ["Overlay.PickerHint"] = "取色模式：点击拾取颜色 · C 退出",
        ["Overlay.PickerCopied"] = "已复制 {0}",
        ["Pin.Hint"] = "拖动移动 · 滚轮缩放 · 双击或 Esc 关闭",
        ["Ring.Cancel"] = "取消 (Esc)",
        ["Ring.Confirm"] = "确认 (Enter)",

        ["Common.Save"] = "保存",
        ["Common.Cancel"] = "取消",
        ["Common.Close"] = "关闭",
        ["Common.Refresh"] = "刷新",
        ["Common.Delete"] = "删除",
        ["Common.Copy"] = "复制",
        ["Common.Open"] = "打开",
        ["Common.None"] = "无",
        ["Common.Solid"] = "纯色",
        ["Common.Gradient"] = "渐变",
        ["Common.Auto"] = "自动",

        // ---- capture / idle ----
        ["Capture.Title"] = "截图",
        ["Capture.Lede"] = "选一个模式，或在任意界面按全局热键。",
        ["Capture.Fullscreen"] = "全屏",
        ["Capture.FullscreenHint"] = "整个虚拟桌面，含所有显示器",
        ["Capture.Region"] = "区域",
        ["Capture.RegionHint"] = "拖一个框，然后标注",
        ["Capture.Window"] = "窗口",
        ["Capture.WindowHint"] = "从实时列表里选一个窗口",
        ["Capture.Paste"] = "粘贴图片",
        ["Capture.PasteHint"] = "把剪贴板里的图直接打开",
        ["Capture.Recent"] = "最近",
        ["Capture.OpenLibrary"] = "打开历史",
        ["Capture.RecentEmpty"] = "截图会出现在这里。",
        ["Capture.Ready"] = "就绪",
        ["Capture.RegionOn"] = "区域截图热键",
        ["Capture.HideToTray"] = "收进托盘",
        ["Capture.Settings"] = "设置",
        ["Capture.TrayHint"] = "右键托盘图标打开设置",
        ["Capture.Language"] = "语言",
        ["Capture.LanguageSystem"] = "跟随系统",

        // ---- editor ----
        ["Editor.Title"] = "编辑器",
        ["Editor.Untitled"] = "未命名截图",
        ["Editor.Back"] = "返回截图",
        ["Editor.Undo"] = "撤销",
        ["Editor.Redo"] = "重做",
        ["Editor.Save"] = "保存",
        ["Editor.SaveTip"] = "导出为 PNG / JPEG / WebP（格式跟随扩展名）",
        ["Editor.ExportDialogTitle"] = "导出图片",
        ["Editor.ExportTypePng"] = "PNG 图片",
        ["Editor.ExportTypeJpeg"] = "JPEG 图片",
        ["Editor.ExportTypeWebp"] = "WebP 图片",
        ["Editor.Copy"] = "复制",
        ["Editor.CopyTip"] = "把合成后的图片复制到剪贴板",
        ["Editor.More"] = "更多操作",
        ["Editor.QuickSave"] = "快速保存",
        ["Editor.Upload"] = "上传到云端",
        ["Editor.Library"] = "截图历史",
        ["Editor.Pin"] = "贴到屏幕",
        ["Editor.CloudSettings"] = "云上传设置…",
        ["Editor.Settings"] = "设置…",
        ["Editor.Delete"] = "删除选中",
        ["Editor.StatusHint"] = "Esc 取消 · Enter 复制",
        ["Editor.CanvasHint"] = "在画布上拖动即可绘制。Ctrl + 滚轮调整当前工具的粗细。",

        // ---- tools ----
        ["Tool.Select"] = "选择 / 移动 / 缩放 (V)",
        ["Tool.Rectangle"] = "矩形 (R)",
        ["Tool.Ellipse"] = "椭圆 (E)",
        ["Tool.Line"] = "直线 (L)",
        ["Tool.ArrowStraight"] = "箭头 (A)",
        ["Tool.ArrowCurved"] = "曲线箭头 (O)",
        ["Tool.Text"] = "文字 (T)",
        ["Tool.Number"] = "序号 (N)",
        ["Tool.Spotlight"] = "聚光灯 (S)",
        ["Tool.Pencil"] = "铅笔 (P)",
        ["Tool.Highlighter"] = "荧光笔 (H)",
        ["Tool.Mosaic"] = "马赛克 (M)",
        ["Tool.Blur"] = "模糊 (B)",
        ["Tool.Invert"] = "反色 (I)",
        ["Tool.Crop"] = "裁剪 (X) — 非破坏性",

        // ---- panel ----
        ["Kind.None"] = "未选中",
        ["Kind.Rectangle"] = "矩形",
        ["Kind.Ellipse"] = "椭圆",
        ["Kind.Line"] = "直线",
        ["Kind.ArrowStraight"] = "箭头",
        ["Kind.ArrowCurved"] = "曲线箭头",
        ["Kind.Text"] = "文字",
        ["Kind.Number"] = "序号",
        ["Kind.Spotlight"] = "聚光灯",
        ["Kind.Pencil"] = "铅笔",
        ["Kind.Highlighter"] = "荧光笔",
        ["Kind.Mosaic"] = "马赛克",
        ["Kind.Blur"] = "模糊",
        ["Kind.Invert"] = "反色",

        ["Panel.Style"] = "样式",
        ["Panel.Canvas"] = "画布",
        ["Panel.Colour"] = "颜色",
        ["Panel.History"] = "取色历史",
        ["Panel.Stroke"] = "粗细",
        ["Panel.TextSize"] = "字号",
        ["Panel.Selection"] = "选中",
        ["Panel.Opacity"] = "不透明度",
        ["Panel.Position"] = "位置",
        ["Panel.Background"] = "背景",
        ["Panel.BackgroundAuto"] = "自动（边缘取色）",
        ["Panel.BackgroundAutoTip"] = "取图片边缘的主色作为背景",
        ["Panel.BackgroundNoneTip"] = "透明：画布上的棋盘格区域导出后是透明的（把内边距调成 0 就没有那圈透明边框）",
        ["Panel.Frame"] = "外框",
        ["Panel.Padding"] = "内边距",
        ["Panel.Radius"] = "圆角",
        ["Panel.Shadow"] = "阴影",
        ["Panel.Border"] = "边框",
        ["Panel.Inset"] = "内衬",
        ["Panel.Outside"] = "外侧",
        ["Panel.Center"] = "居中",
        ["Panel.Inside"] = "内侧",
        ["Panel.CropRatio"] = "裁剪比例",
        ["Panel.NoSelection"] = "未选中任何标注",

        // ---- library ----
        ["Library.NoLink"] = "该记录没有上传链接",
        ["Library.LinkCopied"] = "链接已复制",
        ["Library.FileGone"] = "本地文件不存在，也没有远程链接",
        ["Library.NoLocalFile"] = "该记录没有本地文件",
        ["Library.PathCopied"] = "路径已复制",

        ["Library.Title"] = "截图历史",
        ["Library.Search"] = "搜索文件名",
        ["Library.Empty"] = "还没有截图。按下 PrtSc 就会出现在这里。",
        ["Library.CopyLink"] = "复制链接",
        ["Library.CopyPath"] = "复制路径",
        ["Library.OpenImage"] = "打开图片",
        ["Library.Delete"] = "删除",
        ["Library.LocalOnly"] = "仅本地",
        ["Library.Uploaded"] = "{0} · 已上传",
        ["Library.CountOne"] = "1 条记录",
        ["Library.CountMany"] = "{0} 条记录",
        ["Library.DeleteConfirmTitle"] = "确认删除",
        ["Library.DeleteConfirmRecord"] = "确定删除「{0}」？",
        ["Library.DeleteFileStays"] = "图片文件仍保留在磁盘上。",
        ["Library.DeleteRecordOnly"] = "仅删除历史记录。",

        // ---- cloud settings ----
        ["Cloud.NoneTitle"] = "当前不上传",
        ["Cloud.NoneHint"] = "截图只保存到本机。选择上面的渠道即可上传并复制链接。",
        ["Cloud.Testing"] = "正在测试连接…",
        ["Cloud.TestError"] = "连接测试异常：{0}",
        ["Cloud.PickProviderFirst"] = "先选择 R2 或 Google Drive 渠道再测试。",
        ["Cloud.MissingR2"] = "R2 需要填写 Account ID / Bucket / Access Key ID / Secret Access Key。",
        ["Cloud.MissingDrive"] = "Google Drive 需要填写 Client ID / Client Secret / Refresh Token。",
        ["Cloud.Saved"] = "已保存。",

        ["Cloud.Title"] = "云上传",
        ["Cloud.Lede"] = "截图可上传到 Cloudflare R2（S3 兼容）或 Google Drive，上传后链接自动复制。凭据明文保存在本机 config.json，不会外传。",
        ["Cloud.Test"] = "测试连接",
        ["Cloud.TestTip"] = "用当前表单凭据发一次连接测试（R2 = HeadBucket；Drive = 换 token）",
        ["Cloud.Provider"] = "渠道",
        ["Cloud.ProviderNone"] = "不上传",
        ["Cloud.AutoUpload"] = "保存后自动上传",
        ["Cloud.Save"] = "保存",
        ["Cloud.R2Hint"] = "在 Cloudflare 控制台 → R2 → Manage API Tokens 创建 Token，并记录 Account ID。",
        ["Cloud.AccountId"] = "Account ID",
        ["Cloud.Bucket"] = "Bucket",
        ["Cloud.AccessKey"] = "Access Key ID",
        ["Cloud.SecretKey"] = "Secret Access Key",
        ["Cloud.PublicUrl"] = "公开访问域名（可选）",
        ["Cloud.PublicUrlHint"] = "用于生成链接，如 https://img.example.com",
        ["Cloud.Directory"] = "上传目录（可选）",
        ["Cloud.GDriveHint"] = "在 Google Cloud Console 创建 OAuth Desktop 客户端，换取离线 refresh_token。",
        ["Cloud.ClientId"] = "Client ID",
        ["Cloud.ClientSecret"] = "Client Secret",
        ["Cloud.RefreshToken"] = "Refresh Token",
        ["Cloud.FolderId"] = "目标文件夹 ID（可选）",
        ["Cloud.FolderIdHint"] = "留空 = 我的云端硬盘根目录",

        // ---- status bar ----
        ["Status.Ready"] = "就绪",
        ["Status.Captured"] = "已截取 {0} × {1} @ {2}×",
        ["Status.Copied"] = "已复制 {0} × {1} 到剪贴板",
        ["Status.CopyBusy"] = "复制失败：剪贴板被占用",
        ["Status.CopyFailed"] = "复制失败：{0}",
        ["Status.QuickSaveUnavailable"] = "快速保存需要配置文件",
        ["Status.QuickSaved"] = "已保存 {0}",
        ["Status.QuickSavedPath"] = "已保存 {0} — 路径已复制",
        ["Status.QuickSaveFailed"] = "快速保存失败：{0}",
        ["Status.Exported"] = "已保存 {0}",
        ["Status.ExportedCopied"] = "已保存 {0}（并已复制到剪贴板）",
        ["Status.ExportFailedNoPath"] = "导出失败：没有本地文件路径。",
        ["Status.ExportFailed"] = "导出失败：{0}",
        ["Status.UploadFailed"] = "上传失败：{0}",
        ["Status.UploadedUrl"] = "已上传 {0} — 链接已复制",
        ["Status.Uploaded"] = "已上传 {0}",
        ["Status.UploadCanceled"] = "上传已取消",
        ["Status.ClipboardEmpty"] = "剪贴板里没有图片",
        ["Status.ImportFailed"] = "打不开这张图片：{0}",
        ["Status.FileNotFound"] = "找不到文件：{0}",
        ["Status.UnsupportedFormat"] = "不支持的图片格式：{0}",
        ["Status.Pinned"] = "已贴图 {0} × {1}",
        ["Status.PinFailed"] = "贴图失败：{0}",
        ["Status.DelayCanceled"] = "延时截图已取消",
        ["Status.RegionFailed"] = "区域截图失败：{0}",
        ["Status.CaptureFailed"] = "截图失败：{0}",
        ["Status.WindowFailed"] = "窗口截图失败：{0}",

        // ---- N-1 desktop notifications（toast 双行：title + message；开关见 Settings.General.ShowNotification） ----
        ["Notify.Exported.Title"] = "截图已保存",
        ["Notify.Exported.Message"] = "已保存到 {0}",
        ["Notify.QuickSaved.Title"] = "快速保存完成",
        ["Notify.QuickSaved.Message"] = "已保存到 {0}",
        ["Notify.Copied.Title"] = "已复制到剪贴板",
        ["Notify.Copied.Message"] = "{0} × {1} 像素",
        ["Notify.Uploaded.Title"] = "上传完成",
        ["Notify.Uploaded.Message"] = "{0}",

        // ---- delayed capture (D-1) ----
        ["Delay.BadgeCancelHint"] = "按任意截图热键取消",

        // ---- capture launcher (G-26) ----
        ["Launcher.Title"] = "截图启动器",
        ["Launcher.Mode"] = "模式",
        ["Launcher.ModeFullscreen"] = "全屏",
        ["Launcher.ModeRegion"] = "区域",
        ["Launcher.Geometry"] = "区域位置与尺寸",
        ["Launcher.GeometryHint"] = "物理像素、虚拟桌面坐标（多屏时 X/Y 可为负）。超出桌面的值会自动钳回界内。",
        ["Launcher.X"] = "X",
        ["Launcher.Y"] = "Y",
        ["Launcher.W"] = "W",
        ["Launcher.H"] = "H",
        ["Launcher.Delay"] = "延时",
        ["Launcher.Delay0"] = "不延时",
        ["Launcher.Delay3"] = "3 秒",
        ["Launcher.Delay5"] = "5 秒",
        ["Launcher.Start"] = "开始截图",
        ["Launcher.Error.Parse"] = "四个字段都需要输入整数",
        ["Launcher.Error.Size"] = "宽和高必须大于 0",

        // ---- tray ----
        ["Tray.Show"] = "显示 Prtsc",
        ["Tray.CaptureFullscreen"] = "全屏截图",
        ["Tray.CaptureRegion"] = "区域截图",
        ["Tray.CaptureWindow"] = "窗口截图",
        ["Tray.DelayedCapture"] = "延时截图",
        ["Tray.DelayedCapture.3s"] = "3 秒后全屏截图",
        ["Tray.DelayedCapture.5s"] = "5 秒后全屏截图",
        ["Tray.CaptureLauncher"] = "截图启动器…",
        ["Tray.Library"] = "截图历史…",
        ["Tray.CloudSettings"] = "云上传设置…",
        ["Tray.Settings"] = "设置…",
        ["Tray.Quit"] = "退出",

        // ---- settings center (S-1) ----
        ["Settings.Title"] = "设置",
        ["Settings.Nav.General"] = "常规",
        ["Settings.Nav.Hotkeys"] = "热键",
        ["Settings.Nav.QuickSave"] = "快速保存",
        ["Settings.Nav.Export"] = "导出",
        ["Settings.Nav.Editor"] = "编辑器默认值",
        ["Settings.Nav.Capture"] = "捕获",
        ["Settings.General.LaunchOnStartup"] = "开机自启",
        ["Settings.General.MinimizeToTray"] = "启动时隐藏到托盘",
        ["Settings.General.CloseToTray"] = "点 ✕ 隐藏到托盘",
        ["Settings.General.ShowNotification"] = "截图完成通知",
        ["Settings.General.CheckUpdates"] = "启动时检查更新",
        ["Settings.General.FeedUrl"] = "更新 feed 地址",
        ["Settings.General.FeedUrlHint"] = "Velopack feed 基地址——该地址下须能取到 releases.win.json，示例 https://github.com/cholf5/prtsc/releases/latest/download/。留空 = 不检查更新。",
        ["Settings.General.Language"] = "语言",
        ["Settings.General.LangSystem"] = "跟随系统",
        ["Settings.General.LangEn"] = "English",
        ["Settings.General.LangZh"] = "简体中文",
        ["Settings.Hotkeys.Lede"] = "可不带修饰键，单键即可。点击一栏后按下组合键。",
        ["Settings.Hotkeys.Fullscreen"] = "全屏截图",
        ["Settings.Hotkeys.Region"] = "区域截图",
        ["Settings.Hotkeys.Window"] = "窗口截图",
        ["Settings.Hotkeys.Recording"] = "按下组合键，Esc 取消",
        ["Settings.QuickSave.Folder"] = "保存文件夹",
        ["Settings.QuickSave.FolderHint"] = "留空 = ~/Pictures/Prtsc",
        ["Settings.QuickSave.Browse"] = "浏览…",
        ["Settings.QuickSave.Pattern"] = "文件名",
        ["Settings.QuickSave.PatternTimestamp"] = "时间戳（prtsc-20261102-094213）",
        ["Settings.QuickSave.PatternDate"] = "按天（prtsc-20261102）",
        ["Settings.QuickSave.PatternIncrement"] = "递增序号（prtsc-0042）",
        ["Settings.Export.Format"] = "默认格式",
        ["Settings.Export.Png"] = "PNG（无损）",
        ["Settings.Export.Jpeg"] = "JPEG（更小）",
        ["Settings.Export.JpegQuality"] = "JPEG 质量",
        ["Settings.Export.IncludeBackground"] = "包含背景",
        ["Settings.Export.AutoCopy"] = "导出后自动复制到剪贴板",
        ["Settings.Capture.IncludeCursor"] = "包含鼠标光标（Windows）",
        ["Settings.Capture.IncludeCursorHint"] = "仅 Windows 生效（窗口与全屏捕获）；macOS 与 Linux 忽略此项。",
        ["Settings.Capture.Delay"] = "默认延时",
        ["Settings.Capture.Delay0"] = "不延时",
        ["Settings.Capture.Delay3"] = "3 秒",
        ["Settings.Capture.Delay5"] = "5 秒",
        ["Settings.Capture.DelayHint"] = "抓帧前的等待时间——作用于热键/托盘捕获，也作用于托盘「延时截图」子项。倒计时期间按任意截图热键可取消。",
        ["Settings.Editor.Canvas"] = "画布",
        ["Settings.Editor.Padding"] = "边距",
        ["Settings.Editor.CornerRadius"] = "圆角",
        ["Settings.Editor.Shadow"] = "阴影",
        ["Settings.Editor.Ratio"] = "输出比例",
        ["Settings.Editor.ShowBackground"] = "显示背景",
        ["Settings.Editor.BackgroundColor"] = "背景色",
        ["Settings.Editor.Custom"] = "自定义 hex",
        ["Settings.Editor.Inset"] = "内嵌",
        ["Settings.Editor.AutoBackground"] = "自动背景色",
        ["Settings.Editor.InsetBackgroundColor"] = "内嵌背景色",
        ["Settings.Editor.ShapeCornerRadius"] = "图形圆角",
        ["Settings.Editor.Border"] = "描边",
        ["Settings.Editor.BorderEnabled"] = "启用描边",
        ["Settings.Editor.BorderWeight"] = "粗细",
        ["Settings.Editor.BorderColor"] = "颜色",
        ["Settings.Editor.BorderOpacity"] = "不透明度",
        ["Settings.Editor.BorderType"] = "位置",
        ["Settings.Editor.BorderOutside"] = "外侧",
        ["Settings.Editor.BorderCenter"] = "居中",
        ["Settings.Editor.BorderInside"] = "内侧",
        ["Settings.Save"] = "保存",
        ["Settings.Cancel"] = "取消",
        ["Settings.Hint.Saved"] = "已保存",
        ["Settings.Hint.HotkeyInvalid"] = "热键无效——需要一个主键，可搭配 Ctrl/Alt/Shift/Win",
        ["Settings.Hint.HotkeyDuplicate"] = "有两个截图动作共用了同一组热键",
        ["Settings.Hint.AutostartFailed"] = "开机自启设置失败：{0}",

        // ---- update prompt (C-1/R-1 velopack) ----
        ["Update.Prompt.Title"] = "有可用更新",
        ["Update.Prompt.Lede"] = "Prtsc {0} 已发布（当前版本 {1}）。",
        ["Update.Prompt.Install"] = "下载并安装",
        ["Update.Prompt.Skip"] = "跳过此版本",
        ["Update.Prompt.Later"] = "以后再说",
        ["Update.Prompt.Downloading"] = "下载中… {0}%",
        ["Update.Prompt.Restarting"] = "更新已下载，正在重启…",
        ["Update.Prompt.DownloadFailed"] = "更新失败：{0}",
        ["Update.Prompt.NoUpdateOnRetry"] = "feed 上没有找到更新——地址是不是变了？",
        ["Status.UpdateCheckFailed"] = "更新检查失败：{0}",

        // ---- window picker ----
        ["Picker.Title"] = "选择要截取的窗口",
        ["Picker.Lede"] = "选一个窗口，双击直接截取。",
        ["Picker.Capture"] = "截取",
        ["Picker.Empty"] = "没找到可截取的窗口。",
        ["Picker.Untitled"] = "（未命名）",
        ["Picker.Minimized"] = "[已最小化]",
    };
}
