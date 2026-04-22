using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QRCoder;

namespace GestionHoraire.Services
{
    public class TwoFactorService
    {
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        private const string BackupAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public string GenerateSharedKey()
        {
            var bytes = RandomNumberGenerator.GetBytes(20);
            return Base32Encode(bytes);
        }

        public string FormatSharedKey(string sharedKey)
        {
            var normalized = NormalizeSharedKey(sharedKey);
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            var builder = new StringBuilder(normalized.Length + normalized.Length / 4);
            for (var i = 0; i < normalized.Length; i++)
            {
                if (i > 0 && i % 4 == 0)
                    builder.Append(' ');

                builder.Append(normalized[i]);
            }

            return builder.ToString();
        }

        public string BuildOtpAuthUri(string issuer, string accountName, string sharedKey)
        {
            var safeIssuer = issuer?.Trim() ?? "GestionHoraire";
            var safeAccount = string.IsNullOrWhiteSpace(accountName) ? "Compte" : accountName.Trim();
            var encodedIssuer = Uri.EscapeDataString(safeIssuer);
            var encodedAccount = Uri.EscapeDataString(safeAccount);

            return $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={NormalizeSharedKey(sharedKey)}&issuer={encodedIssuer}";
        }

        public string GenerateQrCodeSvg(string payload)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var qrCode = new SvgQRCode(data);
            return qrCode.GetGraphic(12);
        }

        public bool ValidateTotp(string sharedKey, string code, int allowedDriftWindows = 1)
        {
            var normalizedCode = NormalizeTotpCode(code);
            if (normalizedCode.Length != 6)
                return false;

            var key = Base32Decode(NormalizeSharedKey(sharedKey));
            if (key.Length == 0)
                return false;

            var currentTimeStep = GetCurrentTimeStepNumber();
            var expectedBytes = Encoding.ASCII.GetBytes(normalizedCode);

            for (long offset = -allowedDriftWindows; offset <= allowedDriftWindows; offset++)
            {
                var candidate = ComputeTotp(key, currentTimeStep + offset);
                if (CryptographicOperations.FixedTimeEquals(expectedBytes, Encoding.ASCII.GetBytes(candidate)))
                    return true;
            }

            return false;
        }

        public IReadOnlyList<string> GenerateBackupCodes(int count = 8)
        {
            var result = new List<string>(count);
            for (var i = 0; i < count; i++)
                result.Add(CreateBackupCode());

            return result;
        }

        public string NormalizeBackupCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            return new string(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        }

        public byte[] HashBackupCode(string code, Guid salt)
        {
            return HashWithSalt(NormalizeBackupCode(code), salt);
        }

        public byte[] HashWithSalt(string value, Guid salt)
        {
            var saltBytes = salt.ToByteArray();
            var valueBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            var input = new byte[saltBytes.Length + valueBytes.Length];

            Buffer.BlockCopy(saltBytes, 0, input, 0, saltBytes.Length);
            Buffer.BlockCopy(valueBytes, 0, input, saltBytes.Length, valueBytes.Length);

            return SHA256.HashData(input);
        }

        private static string CreateBackupCode()
        {
            var bytes = RandomNumberGenerator.GetBytes(8);
            var chars = new char[9];

            for (var i = 0; i < 4; i++)
                chars[i] = BackupAlphabet[bytes[i] % BackupAlphabet.Length];

            chars[4] = '-';

            for (var i = 0; i < 4; i++)
                chars[5 + i] = BackupAlphabet[bytes[4 + i] % BackupAlphabet.Length];

            return new string(chars);
        }

        private static string NormalizeSharedKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            return new string(key.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        }

        private static string NormalizeTotpCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            return new string(code.Where(char.IsDigit).ToArray());
        }

        private static long GetCurrentTimeStepNumber()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        }

        private static string ComputeTotp(byte[] key, long timestep)
        {
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

            var otp = binaryCode % 1_000_000;
            return otp.ToString("D6");
        }

        private static string Base32Encode(byte[] data)
        {
            if (data.Length == 0)
                return string.Empty;

            var output = new StringBuilder((data.Length * 8 + 4) / 5);
            var buffer = (int)data[0];
            var next = 1;
            var bitsLeft = 8;

            while (bitsLeft > 0 || next < data.Length)
            {
                if (bitsLeft < 5)
                {
                    if (next < data.Length)
                    {
                        buffer <<= 8;
                        buffer |= data[next++] & 0xFF;
                        bitsLeft += 8;
                    }
                    else
                    {
                        var pad = 5 - bitsLeft;
                        buffer <<= pad;
                        bitsLeft += pad;
                    }
                }

                var index = 0x1F & (buffer >> (bitsLeft - 5));
                bitsLeft -= 5;
                output.Append(Base32Alphabet[index]);
            }

            return output.ToString();
        }

        private static byte[] Base32Decode(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Array.Empty<byte>();

            var cleaned = input.TrimEnd('=').ToUpperInvariant();
            var buffer = 0;
            var bitsLeft = 0;
            var output = new List<byte>(cleaned.Length * 5 / 8);

            foreach (var c in cleaned)
            {
                var index = Base32Alphabet.IndexOf(c);
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
}
