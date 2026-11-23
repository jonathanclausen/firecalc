using System.Security.Cryptography;
using System.Text;

namespace FireCalc.Api.Mcp;

/// <summary>Random tokens and codes for the MCP login, and the hashes they are stored as.</summary>
public static class OAuthTokens
{
    public const string AccessPrefix = "fc_at_";
    public const string RefreshPrefix = "fc_rt_";
    public const string CodePrefix = "fc_ac_";

    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromHours(1);
    /// <summary>Each refresh starts a new period, so a connection used now and then stays connected.</summary>
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(90);

    public static string New(string prefix) => prefix + Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string NewClientId() => "fc_client_" + Base64Url(RandomNumberGenerator.GetBytes(16));

    /// <summary>Only hashes are stored, so a leaked database doesn't hand out working tokens.</summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>PKCE S256: base64url(sha256(verifier)) must equal the challenge sent with the authorization.</summary>
    public static bool VerifierMatches(string verifier, string challenge) =>
        verifier.Length is >= 43 and <= 128
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))),
            Encoding.ASCII.GetBytes(challenge));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
