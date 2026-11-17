using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Oidc;

/// <summary>
/// Keys must survive restarts: the signing key is what every other service trusts through the JWKS endpoint, and the
/// encryption key protects authorization codes and refresh tokens. Both are created on first start and stored in a directory
/// (a Docker volume in Compose); without a directory, ephemeral keys are used (tests).
/// </summary>
public sealed class SigningKeys(RsaSecurityKey signing, SymmetricSecurityKey encryption)
{
    public RsaSecurityKey Signing { get; } = signing;
    public SymmetricSecurityKey Encryption { get; } = encryption;

    public static SigningKeys LoadOrCreate(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return new(Rsa(RSA.Create(2048)), new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)));

        Directory.CreateDirectory(directory);
        var rsaPath = Path.Combine(directory, "signing-rsa.pem");
        var aesPath = Path.Combine(directory, "encryption.key");

        var rsa = RSA.Create();
        if (File.Exists(rsaPath)) rsa.ImportFromPem(File.ReadAllText(rsaPath));
        else { rsa = RSA.Create(2048); WritePrivate(rsaPath, rsa.ExportRSAPrivateKeyPem()); }

        byte[] aes;
        if (File.Exists(aesPath)) aes = Convert.FromBase64String(File.ReadAllText(aesPath).Trim());
        else { aes = RandomNumberGenerator.GetBytes(32); WritePrivate(aesPath, Convert.ToBase64String(aes)); }

        return new(Rsa(rsa), new SymmetricSecurityKey(aes));
    }

    private static RsaSecurityKey Rsa(RSA rsa)
    {
        var key = new RsaSecurityKey(rsa);
        key.KeyId = Base64UrlEncoder.Encode(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())[..8]);
        return key;
    }

    private static void WritePrivate(string path, string content)
    {
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
