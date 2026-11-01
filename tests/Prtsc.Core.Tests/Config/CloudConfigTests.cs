using System.Text.Json;
using Prtsc.Core.Config;

namespace Prtsc.Core.Tests;

/// <summary>
/// 云上传配置（M7a）：Provider/AutoUpload + R2/GDrive 凭据字段的 JSON 往返。
/// 凭据明文存 config.json（与 WinShot 心智一致），字段名 camelCase 锁定。
/// </summary>
public class CloudConfigTests
{
    private static PrtscConfig SampleConfig() => new()
    {
        Cloud = new CloudConfig
        {
            Provider = "r2",
            AutoUpload = true,
            R2 = new R2Config
            {
                AccountId = "abc123def456",
                Bucket = "prtsc-shots",
                PublicUrl = "https://img.example.com",
                Directory = "shots",
                AccessKeyId = "AKIDEXAMPLE",
                SecretAccessKey = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY",
            },
            GDrive = new GDriveConfig
            {
                FolderId = "folder-123",
                ClientId = "client-id.apps.googleusercontent.com",
                ClientSecret = "gdrive-secret",
                RefreshToken = "1//refresh-token",
            },
        },
    };

    [Fact]
    public void Defaults_AreNoneNoAutoUploadAndEmptyCredentials()
    {
        var config = new PrtscConfig();

        Assert.Equal("none", config.Cloud.Provider);
        Assert.False(config.Cloud.AutoUpload);
        Assert.Equal(string.Empty, config.Cloud.R2.AccessKeyId);
        Assert.Equal(string.Empty, config.Cloud.R2.SecretAccessKey);
        Assert.Equal(string.Empty, config.Cloud.GDrive.ClientId);
        Assert.Equal(string.Empty, config.Cloud.GDrive.ClientSecret);
        Assert.Equal(string.Empty, config.Cloud.GDrive.RefreshToken);
    }

    [Fact]
    public void RoundTrip_PreservesAllCloudFields()
    {
        var original = SampleConfig();

        var json = JsonSerializer.Serialize(original, PrtscConfigJsonContext.Default.PrtscConfig);
        var parsed = JsonSerializer.Deserialize(json, PrtscConfigJsonContext.Default.PrtscConfig);

        Assert.NotNull(parsed);
        Assert.Equal(original.Cloud.Provider, parsed!.Cloud.Provider);
        Assert.Equal(original.Cloud.AutoUpload, parsed.Cloud.AutoUpload);
        Assert.Equal(original.Cloud.R2.AccountId, parsed.Cloud.R2.AccountId);
        Assert.Equal(original.Cloud.R2.Bucket, parsed.Cloud.R2.Bucket);
        Assert.Equal(original.Cloud.R2.PublicUrl, parsed.Cloud.R2.PublicUrl);
        Assert.Equal(original.Cloud.R2.Directory, parsed.Cloud.R2.Directory);
        Assert.Equal(original.Cloud.R2.AccessKeyId, parsed.Cloud.R2.AccessKeyId);
        Assert.Equal(original.Cloud.R2.SecretAccessKey, parsed.Cloud.R2.SecretAccessKey);
        Assert.Equal(original.Cloud.GDrive.FolderId, parsed.Cloud.GDrive.FolderId);
        Assert.Equal(original.Cloud.GDrive.ClientId, parsed.Cloud.GDrive.ClientId);
        Assert.Equal(original.Cloud.GDrive.ClientSecret, parsed.Cloud.GDrive.ClientSecret);
        Assert.Equal(original.Cloud.GDrive.RefreshToken, parsed.Cloud.GDrive.RefreshToken);
    }

    [Fact]
    public void Json_UsesCamelCaseNames()
    {
        var json = JsonSerializer.Serialize(SampleConfig(), PrtscConfigJsonContext.Default.PrtscConfig);

        Assert.Contains("\"provider\": \"r2\"", json);
        Assert.Contains("\"autoUpload\": true", json);
        Assert.Contains("\"accessKeyId\"", json);
        Assert.Contains("\"secretAccessKey\"", json);
        Assert.Contains("\"clientId\"", json);
        Assert.Contains("\"clientSecret\"", json);
        Assert.Contains("\"refreshToken\"", json);
    }

    [Fact]
    public void Deserialize_OldConfigWithoutCloudFields_UsesDefaults()
    {
        // 旧版 config.json 没有 provider/autoUpload/凭据节点 → 反序列化不炸、落到默认值
        const string json = """
            {
              "cloud": {
                "r2": { "accountId": "old-account", "bucket": "old-bucket" },
                "gdrive": { "folderId": "old-folder" }
              }
            }
            """;

        var parsed = JsonSerializer.Deserialize(json, PrtscConfigJsonContext.Default.PrtscConfig);

        Assert.NotNull(parsed);
        Assert.Equal("none", parsed!.Cloud.Provider);
        Assert.False(parsed.Cloud.AutoUpload);
        Assert.Equal("old-account", parsed.Cloud.R2.AccountId);
        Assert.Equal("old-bucket", parsed.Cloud.R2.Bucket);
        Assert.Equal(string.Empty, parsed.Cloud.R2.AccessKeyId);
        Assert.Equal("old-folder", parsed.Cloud.GDrive.FolderId);
        Assert.Equal(string.Empty, parsed.Cloud.GDrive.RefreshToken);
    }
}
