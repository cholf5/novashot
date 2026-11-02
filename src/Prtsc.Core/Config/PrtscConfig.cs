using System.Text.Json.Serialization;

namespace Prtsc.Core.Config;

/// <summary>
/// 应用全部持久化配置。JSON 存于 %AppData%\Prtsc\config.json。
/// 字段对齐 WinShot config.go 结构以复用心智模型，但命名空间独立。
/// </summary>
public sealed class PrtscConfig
{
    public HotkeyConfig Hotkeys { get; set; } = new();
    public StartupConfig Startup { get; set; } = new();
    public QuickSaveConfig QuickSave { get; set; } = new();
    public ExportConfig Export { get; set; } = new();
    public WindowConfig Window { get; set; } = new();
    public CaptureConfig Capture { get; set; } = new();
    public EditorConfig Editor { get; set; } = new();
    public UpdateConfig Update { get; set; } = new();
    public CloudConfig Cloud { get; set; } = new();

    /// <summary>用户自定义背景图（data URL），最多 8 张。</summary>
    public List<string> BackgroundImages { get; set; } = new();

    /// <summary>界面语言：system（跟随系统）| en | zh-CN。仅支持简体中文与英文。</summary>
    public string Language { get; set; } = "system";
}

public sealed class HotkeyConfig
{
    public string Fullscreen { get; set; } = "PrintScreen";
    public string Region { get; set; } = "Ctrl+PrintScreen";
    public string Window { get; set; } = "Ctrl+Shift+PrintScreen";

    /// <summary>三条规格（逐字）是否相同。App 层用它跳过无变化的重注册（任何 store.Update 都会触发 Changed）。</summary>
    public bool SameSpecsAs(HotkeyConfig? other)
        => other is not null
           && Fullscreen == other.Fullscreen
           && Region == other.Region
           && Window == other.Window;
}

public sealed class StartupConfig
{
    public bool LaunchOnStartup { get; set; } = false;
    public bool MinimizeToTray { get; set; } = false;
    public bool ShowNotification { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
}

public sealed class QuickSaveConfig
{
    public string Folder { get; set; } = string.Empty; // 空 = 默认 ~/Pictures/Prtsc
    public string Pattern { get; set; } = "timestamp"; // timestamp | date | increment
}

public sealed class ExportConfig
{
    public string DefaultFormat { get; set; } = "png"; // png | jpeg
    public int JpegQuality { get; set; } = 95;
    public bool IncludeBackground { get; set; } = true;
    public bool AutoCopyToClipboard { get; set; } = true;
}

public sealed class WindowConfig
{
    public int Width { get; set; } = 1200;
    public int Height { get; set; } = 800;
}

/// <summary>
/// 捕获行为（S-1）。IncludeCursor 仅作用于 Windows 捕获（窗口与全屏都走 WGC）；mac/Linux 平台忽略该值。
/// </summary>
public sealed class CaptureConfig
{
    /// <summary>截图是否把鼠标光标包含进画面（WGC IsCursorCaptureEnabled；仅 Windows 生效）。</summary>
    public bool IncludeCursor { get; set; } = true;
}

public sealed class EditorConfig
{
    public int Padding { get; set; } = 40;
    public int CornerRadius { get; set; } = 12;
    public int ShadowSize { get; set; } = 20;

    /// <summary>CSS 风格描述：linear-gradient(...) / 十六进制 / url(...)。</summary>
    public string BackgroundColor { get; set; } = "linear-gradient(135deg, #667eea 0%, #764ba2 100%)";

    public string OutputRatio { get; set; } = "auto"; // auto|1:1|4:3|3:2|16:9|5:3|9:16|3:4|2:3
    public bool ShowBackground { get; set; } = true;

    public int Inset { get; set; } = 0;
    public bool AutoBackground { get; set; } = true;
    public string InsetBackgroundColor { get; set; } = "#ffffff";

    public int ShapeCornerRadius { get; set; } = 0;

    public bool BorderEnabled { get; set; } = false;
    public int BorderWeight { get; set; } = 2;
    public string BorderColor { get; set; } = "#000000";
    public int BorderOpacity { get; set; } = 100;
    public string BorderType { get; set; } = "center"; // outside|center|inside
}

public sealed class UpdateConfig
{
    public bool CheckOnStartup { get; set; } = true;
    public string SkippedVersion { get; set; } = string.Empty;
}

public sealed class CloudConfig
{
    /// <summary>上传渠道：none | r2 | gdrive。</summary>
    public string Provider { get; set; } = "none";

    /// <summary>编辑器保存/复制后自动上传（M7b UI 接线；数据层默认关）。</summary>
    public bool AutoUpload { get; set; } = false;

    public R2Config R2 { get; set; } = new();

    // camelCase 策略会把 GDrive 写成 "gDrive"，这里钉成 "gdrive"（WinShot 心智）。
    [JsonPropertyName("gdrive")]
    public GDriveConfig GDrive { get; set; } = new();
}

public sealed class R2Config
{
    public string AccountId { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;

    /// <summary>R2 API Token 的 Access Key ID（凭据明文存 config.json，与 WinShot 心智一致）。</summary>
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
}

public sealed class GDriveConfig
{
    public string FolderId { get; set; } = string.Empty;

    /// <summary>OAuth Desktop 凭据 + 离线 refresh_token（凭据明文存 config.json，与 WinShot 心智一致）。</summary>
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(PrtscConfig))]
public partial class PrtscConfigJsonContext : JsonSerializerContext;
