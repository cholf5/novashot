using CommunityToolkit.Mvvm.ComponentModel;
using Novashot.Core.Config;

namespace Novashot.App.ViewModels;

/// <summary>云上传设置窗 VM：读 config → 编辑 → 保存回写（Update + SaveAsync）。</summary>
public sealed partial class CloudSettingsViewModel : ObservableObject
{
    private readonly IConfigStore _store;

    [ObservableProperty]
    private int _providerIndex; // 0 none / 1 r2 / 2 gdrive

    [ObservableProperty]
    private bool _autoUpload;

    [ObservableProperty]
    private string _r2AccountId = "";
    [ObservableProperty]
    private string _r2Bucket = "";
    [ObservableProperty]
    private string _r2AccessKeyId = "";
    [ObservableProperty]
    private string _r2SecretAccessKey = "";
    [ObservableProperty]
    private string _r2PublicUrl = "";
    [ObservableProperty]
    private string _r2Directory = "";

    [ObservableProperty]
    private string _gDriveClientId = "";
    [ObservableProperty]
    private string _gDriveClientSecret = "";
    [ObservableProperty]
    private string _gDriveRefreshToken = "";
    [ObservableProperty]
    private string _gDriveFolderId = "";

    [ObservableProperty]
    private string _saveHint = "";

    public CloudSettingsViewModel(IConfigStore store)
    {
        _store = store;
        LoadFromConfig();
    }

    public bool IsR2Visible => ProviderIndex == 1;
    public bool IsGDriveVisible => ProviderIndex == 2;

    /// <summary>None 渠道时的空状态（面板不至于空一大片）。</summary>
    public bool IsNoneVisible => ProviderIndex == 0;

    partial void OnProviderIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsR2Visible));
        OnPropertyChanged(nameof(IsGDriveVisible));
        OnPropertyChanged(nameof(IsNoneVisible));
    }

    private void LoadFromConfig()
    {
        var cloud = _store.Current.Cloud;
        ProviderIndex = cloud.Provider?.Trim().ToLowerInvariant() switch
        {
            "r2" => 1,
            "gdrive" => 2,
            _ => 0,
        };
        AutoUpload = cloud.AutoUpload;
        R2AccountId = cloud.R2.AccountId;
        R2Bucket = cloud.R2.Bucket;
        R2AccessKeyId = cloud.R2.AccessKeyId;
        R2SecretAccessKey = cloud.R2.SecretAccessKey;
        R2PublicUrl = cloud.R2.PublicUrl;
        R2Directory = cloud.R2.Directory;
        GDriveClientId = cloud.GDrive.ClientId;
        GDriveClientSecret = cloud.GDrive.ClientSecret;
        GDriveRefreshToken = cloud.GDrive.RefreshToken;
        GDriveFolderId = cloud.GDrive.FolderId;
    }

    /// <summary>把界面值写回 config 并持久化。返回是否通过基础校验。</summary>
    public bool Save()
    {
        var provider = ProviderIndex switch
        {
            1 => "r2",
            2 => "gdrive",
            _ => "none",
        };

        if (provider == "r2" &&
            (string.IsNullOrWhiteSpace(R2AccountId) || string.IsNullOrWhiteSpace(R2Bucket) ||
             string.IsNullOrWhiteSpace(R2AccessKeyId) || string.IsNullOrWhiteSpace(R2SecretAccessKey)))
        {
            SaveHint = Novashot.App.Localization.Loc.T("Cloud.MissingR2");
            return false;
        }
        if (provider == "gdrive" &&
            (string.IsNullOrWhiteSpace(GDriveClientId) || string.IsNullOrWhiteSpace(GDriveClientSecret) ||
             string.IsNullOrWhiteSpace(GDriveRefreshToken)))
        {
            SaveHint = Novashot.App.Localization.Loc.T("Cloud.MissingDrive");
            return false;
        }

        _store.Update(c =>
        {
            c.Cloud.Provider = provider;
            c.Cloud.AutoUpload = AutoUpload;
            c.Cloud.R2.AccountId = R2AccountId.Trim();
            c.Cloud.R2.Bucket = R2Bucket.Trim();
            c.Cloud.R2.AccessKeyId = R2AccessKeyId.Trim();
            c.Cloud.R2.SecretAccessKey = R2SecretAccessKey.Trim();
            c.Cloud.R2.PublicUrl = R2PublicUrl.Trim();
            c.Cloud.R2.Directory = R2Directory.Trim();
            c.Cloud.GDrive.ClientId = GDriveClientId.Trim();
            c.Cloud.GDrive.ClientSecret = GDriveClientSecret.Trim();
            c.Cloud.GDrive.RefreshToken = GDriveRefreshToken.Trim();
            c.Cloud.GDrive.FolderId = GDriveFolderId.Trim();
        });
        _ = _store.SaveAsync();
        SaveHint = Novashot.App.Localization.Loc.T("Cloud.Saved");
        return true;
    }

    /// <summary>表单当前 R2 凭据（连接测试用，不落盘）。</summary>
    public R2Config ToR2Config() => new()
    {
        AccountId = R2AccountId.Trim(),
        Bucket = R2Bucket.Trim(),
        AccessKeyId = R2AccessKeyId.Trim(),
        SecretAccessKey = R2SecretAccessKey.Trim(),
        PublicUrl = R2PublicUrl.Trim(),
        Directory = R2Directory.Trim(),
    };

    /// <summary>表单当前 GDrive 凭据（连接测试用，不落盘）。</summary>
    public GDriveConfig ToGDriveConfig() => new()
    {
        ClientId = GDriveClientId.Trim(),
        ClientSecret = GDriveClientSecret.Trim(),
        RefreshToken = GDriveRefreshToken.Trim(),
        FolderId = GDriveFolderId.Trim(),
    };
}
