using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

namespace Mermer.Api.Services;

public record LicensePayload(
    string ClientId,
    string ClientName,
    string MachineId,
    DateTime ValidTill,
    string Signature
);

public static class LicenseManager
{
    // Открытый ключ ECDSA Леона (строка из файла ecdsa_public_key.txt)
    public const string PublicKeyBase64 = "8y3wtTS6b4HBcCzmzMhfYvQjLk/J2V6MvF4NcSVflqYajSBT1d5LcCRuPeb1VyJk4cPmYujs0F8a+SOlZmGyDw==";

    public static string CurrentClientId { get; private set; } = "default";
    public static string CurrentClientName { get; private set; } = string.Empty;
    public static bool IsLicenseValid { get; private set; } = false;

    public static bool LoadAndValidate(string licenseFilePath, string currentMachineId)
    {
        if (!File.Exists(licenseFilePath))
            return false;

        try
        {
            var json = File.ReadAllText(licenseFilePath);
            var license = JsonSerializer.Deserialize<LicensePayload>(json);
            if (license == null) return false;

            // 1. Проверка срока годности
            if (license.ValidTill < DateTime.UtcNow)
                return false;

            // 2. Проверка привязки к оборудованию
            if (!string.Equals(license.MachineId, "ANY", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(license.MachineId, currentMachineId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 3. Проверка криптографической подписи ECDSA
            var validTillStr = license.ValidTill.ToString("yyyy-MM-dd");
            var dataToVerify = $"{license.ClientId}|{license.MachineId}|{validTillStr}";
            if (!VerifySignature(dataToVerify, license.Signature))
                return false;

            CurrentClientId = license.ClientId;
            CurrentClientName = license.ClientName;
            IsLicenseValid = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Автоматически проставляет ClientId для всех старых записей из патчера Couchbase, 
    /// где client_id был null или 'default'.
    /// </summary>
    public static async Task EnsureLegacyDataMigratedAsync(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(CurrentClientId) || CurrentClientId == "default")
            return;

        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = @"
                UPDATE invoices SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE invoice_lines SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE stock_slips SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE stock_slip_lines SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE stock_transfers SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE stock_transfer_lines SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE funds_slips SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE funds_slip_lines SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE funds_transfers SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE funds_transfer_lines SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE expense_slips SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE expense_slip_lines SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE partners SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE stocks SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE stock_prices SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
                UPDATE stock_units SET client_id = @clientId WHERE client_id IS NULL OR client_id = '' OR client_id = 'default';
            ";

            await conn.ExecuteAsync(sql, new { clientId = CurrentClientId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LicenseManager] Ошибка автомиграции ClientId: {ex.Message}");
        }
    }

    private static bool VerifySignature(string data, string signatureBase64)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            var sig = Convert.FromBase64String(signatureBase64);

            using var ecdsa = CreateEcdsaFromPublicKey(PublicKeyBase64);
            return ecdsa.VerifyData(bytes, sig, HashAlgorithmName.SHA256);
        }
        catch
        {
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
}