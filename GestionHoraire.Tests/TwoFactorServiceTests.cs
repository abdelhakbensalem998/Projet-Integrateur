using System.Security.Cryptography;
using System.Text;
using GestionHoraire.Services;
using Xunit;

namespace GestionHoraire.Tests;

public class TwoFactorServiceTests
{
    [Fact]
    public void BuildOtpAuthUri_EncodesIssuerAccountAndSecret()
    {
        var service = new TwoFactorService();
        var secret = service.GenerateSharedKey();

        var uri = service.BuildOtpAuthUri("Gestion Horaire", "prof@example.com", secret);

        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("Gestion%20Horaire:prof%40example.com", uri);
        Assert.Contains("issuer=Gestion%20Horaire", uri);
        Assert.Contains($"secret={secret}", uri);
    }

    [Fact]
    public void GenerateQrCodeSvg_ReturnsSvgContent()
    {
        var service = new TwoFactorService();

        var svg = service.GenerateQrCodeSvg("otpauth://totp/GestionHoraire:test?secret=ABC&issuer=GestionHoraire");

        Assert.Contains("<svg", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("</svg>", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateTotp_AcceptsCurrentCodeAndRejectsInvalidCode()
    {
        var service = new TwoFactorService();
        const string secret = "JBSWY3DPEHPK3PXP";
        var validCode = ComputeTotp(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);

        Assert.True(service.ValidateTotp(secret, validCode));
        Assert.False(service.ValidateTotp(secret, "000000"));
        Assert.False(service.ValidateTotp(secret, "abc123"));
    }

    [Fact]
    public void GenerateBackupCodes_ReturnsUniqueCodesWithExpectedFormat()
    {
        var service = new TwoFactorService();

        var codes = service.GenerateBackupCodes(8);

        Assert.Equal(8, codes.Count);
        Assert.Equal(8, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(codes, code => Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", code));
    }

    [Fact]
    public void BackupCodeHashing_NormalizesInputAndUsesSalt()
    {
        var service = new TwoFactorService();
        var salt = Guid.NewGuid();
        var otherSalt = Guid.NewGuid();

        var firstHash = service.HashBackupCode("abCd-2345", salt);
        var normalizedHash = service.HashBackupCode("ABCD2345", salt);
        var differentSaltHash = service.HashBackupCode("ABCD2345", otherSalt);

        Assert.Equal("ABCD2345", service.NormalizeBackupCode("ab cd-2345"));
        Assert.Equal(firstHash, normalizedHash);
        Assert.NotEqual(firstHash, differentSaltHash);
    }

    private static string ComputeTotp(string sharedKey, long timestep)
    {
        var key = Base32Decode(sharedKey);
        var counter = BitConverter.GetBytes(timestep);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter);
        var offset = hash[^1] & 0x0F;

        var binaryCode =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        return (binaryCode % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleaned = input.TrimEnd('=').ToUpperInvariant();
        var buffer = 0;
        var bitsLeft = 0;
        var output = new List<byte>(cleaned.Length * 5 / 8);

        foreach (var c in cleaned)
        {
            var index = alphabet.IndexOf(c);
            if (index < 0)
                continue;

            buffer <<= 5;
            buffer |= index & 0x1F;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                output.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }

        return output.ToArray();
    }
}
