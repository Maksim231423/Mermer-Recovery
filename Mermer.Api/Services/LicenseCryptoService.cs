using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mermer.Api.Services;

public record SignedLicenseData(
    string Modules,
    DateTime ValidTill,
    string? MachineId,
    string? ClientId
);

public interface ILicenseCryptoService
{
    bool TryValidateKey(string licenseKey, out SignedLicenseData? data, out string error);
}

public class LicenseCryptoService : ILicenseCryptoService
{
    // Открытый ключ ECDSA P-256 (88 символов)
    public const string PublicKeyBase64 = "8y3wtTS6b4HBcCzmzMhfYvQjLk/J2V6MvF4NcSVflqYajSBT1d5LcCRuPeb1VyJk4cPmYujs0F8a+SOlZmGyDw==";

    // Идентификаторы модулей Mermer
    public const string ClientModuleId = "dc60017b-9b20-46ca-8b2e-646de9965a9e";
    public const string ServerModuleId = "9a953aa5-2fd9-418d-bcf7-fb5bd7d09553";
    public const string SyncModuleId = "6b1495a1-60aa-4420-9c30-94718c121c26";
    public const string MobileModuleId = "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d";

    public bool TryValidateKey(string licenseKey, out SignedLicenseData? data, out string error)
    {
        data = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            error = "License key is empty.";
            return false;
        }

        var parts = licenseKey.Trim().Split('.');
        if (parts.Length != 2)
        {
            error = "Invalid license key format. Signature missing.";
            return false;
        }

        try
        {
            var payloadBytes = Base64UrlDecode(parts[0]);
            var signatureBytes = Base64UrlDecode(parts[1]);

            if (signatureBytes.Length != 64)
            {
                error = "Invalid signature length for ECDSA P-256.";
                return false;
            }

            // 1. Проверяем цифровую подпись ECDSA P-256
            using var ecdsa = CreateEcdsaFromPublicKey(PublicKeyBase64);
            bool isVerified = ecdsa.VerifyData(
                payloadBytes,
                signatureBytes,
                HashAlgorithmName.SHA256);

            if (!isVerified)
            {
                error = "Tampered or forged license key signature. Verification failed.";
                return false;
            }

            // 2. Десериализуем компактный JSON
            using var doc = JsonDocument.Parse(payloadBytes);
            var root = doc.RootElement;

            string m = root.GetProperty("m").GetString() ?? "all";
            string v = root.GetProperty("v").GetString() ?? "";
            string h = root.TryGetProperty("h", out var hProp) ? hProp.GetString() : "ANY";
            string c = root.TryGetProperty("c", out var cProp) ? cProp.GetString() : "";

            // Строгий парсинг ISO yyyy-MM-dd в UTC без региональных сдвигов
            DateTime validTillDate;
            if (!DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out validTillDate))
            {
                if (!DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out validTillDate))
                {
                    error = "Invalid expiration date in license.";
                    return false;
                }
            }

            // Лицензия действует до конца указанного дня (23:59:59.999 UTC)
            var validTillUtc = validTillDate.Date.AddDays(1).AddTicks(-1);

            if (validTillUtc < DateTime.UtcNow)
            {
                error = "License validity period has expired.";
                return false;
            }

            // 3. Раскрываем короткие коды в GUID модулей Mermer
            string fullModules = m.ToLowerInvariant() switch
            {
                "cs" => $"{ClientModuleId},{ServerModuleId}",
                "c" => ClientModuleId,
                "sync" => SyncModuleId,
                "mobile" => MobileModuleId,
                _ => $"{ClientModuleId},{ServerModuleId},{SyncModuleId},{MobileModuleId}"
            };

            data = new SignedLicenseData(fullModules, validTillUtc, h, c);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Cryptographic validation error: {ex.Message}";
            return false;
        }
    }

    private static ECDsa CreateEcdsaFromPublicKey(string pubBase64)
    {
        byte[] raw = Convert.FromBase64String(pubBase64);
        byte[] x = new byte[32];
        byte[] y = new byte[32];
        Buffer.BlockCopy(raw, 0, x, 0, 32);
        Buffer.BlockCopy(raw, 32, y, 0, 32);

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y }
        };

        var ecdsa = ECDsa.Create();
        ecdsa.ImportParameters(parameters);
        return ecdsa;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        string output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        return Convert.FromBase64String(output);
    }
}