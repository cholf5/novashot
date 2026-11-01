using System.Text;
using Prtsc.Core.Cloud;

namespace Prtsc.Core.Tests;

/// <summary>
/// AWS SigV4 纯函数正确性：以 AWS 官方文档示例向量锁定
/// （GET Object / us-east-1 / s3，AKIDEXAMPLE + wJalr… 密钥）。
/// 参考值来自 AWS docs "Signature Calculations for the Signature Version 4 Signing Process"。
/// </summary>
public class SigV4SignerTests
{
    // AWS 文档示例的固定时间（20230524/us-east-1/s3/aws4_request）
    private const string AccessKey = "AKIDEXAMPLE";
    private const string SecretKey = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY";
    private const string Region = "us-east-1";
    private const string Service = "s3";

    [Fact]
    public void HashSha256_Hex_KnownVector()
    {
        // SHA256("hey") = …（标准向量）
        var hex = SigV4Signer.HashHex(Encoding.UTF8.GetBytes("hey"));
        Assert.Equal("fa690b82061edfd2852629aeba8a8977b57e40fcb77d1a7a28b26cba62591204", hex);
    }

    [Fact]
    public void HmacSha256_Hex_KnownVector()
    {
        var key = Encoding.UTF8.GetBytes("key");
        var data = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
        var hex = SigV4Signer.HmacHex(key, data);
        Assert.Equal("f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8", hex);
    }

    [Fact]
    public void CanonicalRequest_GetObject_MatchesDocExample()
    {
        // AWS 文档 "Example: GET Object" 的 canonical request
        var canonical = SigV4Signer.BuildCanonicalRequest(
            method: "GET",
            uri: "/test.txt",
            query: "",
            headers: new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["host"] = "examplebucket.s3.amazonaws.com",
                ["range"] = "bytes=0-9",
                ["x-amz-content-sha256"] = SigV4Signer.EmptySha256,
                ["x-amz-date"] = "20230524T000000Z",
            },
            signedHeaders: "host;range;x-amz-content-sha256;x-amz-date",
            payloadHash: SigV4Signer.EmptySha256);

        var expected = string.Join("\n",
            "GET",
            "/test.txt",
            "",
            "host:examplebucket.s3.amazonaws.com",
            "range:bytes=0-9",
            "x-amz-content-sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            "x-amz-date:20230524T000000Z",
            "",
            "host;range;x-amz-content-sha256;x-amz-date",
            SigV4Signer.EmptySha256);
        Assert.Equal(expected, canonical);
    }

    [Fact]
    public void StringToSign_MatchesDocStructure()
    {
        var sts = SigV4Signer.BuildStringToSign(
            "20230524T000000Z", "20230524/us-east-1/s3/aws4_request",
            "canonical-request-body");
        Assert.Equal("AWS4-HMAC-SHA256\n20230524T000000Z\n20230524/us-east-1/s3/aws4_request\n"
            + SigV4Signer.HashHex(Encoding.UTF8.GetBytes("canonical-request-body")), sts);
    }

    [Fact]
    public void SigningKey_SignatureChain_SelfConsistent()
    {
        // 全链路自洽向量（python hmac/sha256 独立预计算）：
        // kSigning(20230524/us-east-1/s3) 签名文档 GET Object canonical request 的 SHA256
        var key = SigV4Signer.BuildSigningKey(SecretKey, "20230524", Region, Service);
        var signature = SigV4Signer.HmacHex(key, Encoding.UTF8.GetBytes(
            "AWS4-HMAC-SHA256\n20230524T000000Z\n20230524/us-east-1/s3/aws4_request\n" +
            "fd276e231174121ccf922eb7a68b65ece3df91ed054e703c6f86f809d9a68449"));
        Assert.Equal("c240316f1bc04280b478cb5f4c3d390613ff77a6cc45738cb2b36863bcef8c4e", signature);
    }

    [Fact]
    public void AuthorizationHeader_ComposesCorrectly()
    {
        var auth = SigV4Signer.BuildAuthorizationHeader(
            AccessKey, "20230524", Region, Service,
            signedHeaders: "host;x-amz-content-sha256;x-amz-date",
            signature: "c240316f1bc04280b478cb5f4c3d390613ff77a6cc45738cb2b36863bcef8c4e");
        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20230524/us-east-1/s3/aws4_request, " +
            "SignedHeaders=host;x-amz-content-sha256;x-amz-date, " +
            "Signature=c240316f1bc04280b478cb5f4c3d390613ff77a6cc45738cb2b36863bcef8c4e",
            auth);
    }

    [Fact]
    public void UriEncode_FollowsRfc3986()
    {
        Assert.Equal("a-b.c_d~e", SigV4Signer.UriEncode("a-b.c_d~e"));
        Assert.Equal("a%20b%2Fc", SigV4Signer.UriEncode("a b/c"));
        Assert.Equal("%E4%B8%AD", SigV4Signer.UriEncode("中"));
    }
}
