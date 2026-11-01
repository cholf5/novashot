using System.Security.Cryptography;
using System.Text;

namespace Prtsc.Core.Cloud;

/// <summary>
/// AWS Signature Version 4 纯函数（R2 S3 兼容 API 用）。
/// 只实现我们需要的子集：单值 header（无重复键）、空/字节 payload、URI path 无查询或已编码查询。
/// </summary>
public static class SigV4Signer
{
    public const string EmptySha256 =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public static string HashHex(byte[] data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));

    public static string HmacHex(byte[] key, byte[] data) =>
        Convert.ToHexStringLower(new HMACSHA256(key).ComputeHash(data));

    /// <summary>RFC 3986 unreserved：A-Z a-z 0-9 - _ . ~ 原样，其余 %XX 大写。</summary>
    public static string UriEncode(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            if ((b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9') ||
                b == '-' || b == '_' || b == '.' || b == '~')
                sb.Append((char)b);
            else
                sb.Append('%').Append(Convert.ToHexString([b]));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Canonical request：HTTP 方法 / URI / 查询 / 每行 key:value（已排序）后的空行 / signed headers / payload hash。
    /// headers 必须已按 key 的 byte 序排序且 key 小写。
    /// </summary>
    public static string BuildCanonicalRequest(
        string method, string uri, string query,
        SortedDictionary<string, string> headers, string signedHeaders, string payloadHash)
    {
        var sb = new StringBuilder();
        sb.Append(method).Append('\n');
        sb.Append(uri).Append('\n');
        sb.Append(query).Append('\n');
        foreach (var (k, v) in headers)
            sb.Append(k).Append(':').Append(v.Trim()).Append('\n');
        sb.Append('\n');
        sb.Append(signedHeaders).Append('\n');
        sb.Append(payloadHash);
        return sb.ToString();
    }

    /// <summary>String to sign：算法 \n amzDate \n scope \n hash(canonicalRequest)。</summary>
    public static string BuildStringToSign(string amzDate, string scope, string canonicalRequest) =>
        $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{HashHex(Encoding.UTF8.GetBytes(canonicalRequest))}";

    /// <summary>kSigning = HMAC(HMAC(HMAC("AWS4"+secret, date), region), service) + "aws4_request"。</summary>
    public static byte[] BuildSigningKey(string secretKey, string dateStamp, string region, string service)
    {
        byte[] kDate = new HMACSHA256(Encoding.UTF8.GetBytes("AWS4" + secretKey))
            .ComputeHash(Encoding.UTF8.GetBytes(dateStamp));
        byte[] kRegion = new HMACSHA256(kDate).ComputeHash(Encoding.UTF8.GetBytes(region));
        byte[] kService = new HMACSHA256(kRegion).ComputeHash(Encoding.UTF8.GetBytes(service));
        return new HMACSHA256(kService).ComputeHash(Encoding.UTF8.GetBytes("aws4_request"));
    }

    /// <summary>Authorization 头：算法 Credential=…/scope, SignedHeaders=…, Signature=…。</summary>
    public static string BuildAuthorizationHeader(
        string accessKey, string dateStamp, string region, string service,
        string signedHeaders, string signature) =>
        $"AWS4-HMAC-SHA256 Credential={accessKey}/{dateStamp}/{region}/{service}/aws4_request, " +
        $"SignedHeaders={signedHeaders}, Signature={signature}";
}
