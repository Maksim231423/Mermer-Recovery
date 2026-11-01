using System;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Binyat.Ui.Pc.Services;

public class LicensePayload
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public DateTime ValidTill { get; set; }
    public string Signature { get; set; } = string.Empty;
}

public static class LicenseManager
{
    // Открытый ключ ECDSA Леона (должен точно совпадать со строкой из ecdsa_public_key.txt)
    public const string PublicKeyBase64 = "8y3wtTS6b4HBcCzmzMhfYvQjLk/J2V6MvF4NcSVflqYajSBT1d5LcCRuPeb1VyJk4cPmYujs0F8a+SOlZmGyDw==";

    public static string CurrentClientId { get; private set; } = "default";
    public static string CurrentClientName { get; private set; } = string.Empty;
    public static DateTime ValidTill { get; private set; }
    public static bool IsLicenseValid { get; private set; } = false;

    public static string DefaultLicensePath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "license.lic");

    public static bool CheckLicense(string? customPath = null)
    {
        var path = customPath ?? DefaultLicensePath;
        var machineId = GetMachineId();
        return LoadAndValidate(path, machineId);
    }

    public static bool LoadAndValidate(string licenseFilePath, string currentMachineId)
    {
        if (!File.Exists(licenseFilePath))
            return false;

        try
        {
            var json = File.ReadAllText(licenseFilePath);
            var license = JsonSerializer.Deserialize<LicensePayload>(json);
            if (license == null) return false;

            if (license.ValidTill < DateTime.UtcNow)
                return false;

            if (!string.Equals(license.MachineId, "ANY", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(license.MachineId, currentMachineId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var validTillStr = license.ValidTill.ToString("yyyy-MM-dd");
            var dataToVerify = $"{license.ClientId}|{license.MachineId}|{validTillStr}";
            if (!VerifySignature(dataToVerify, license.Signature))
                return false;

            CurrentClientId = license.ClientId;
            CurrentClientName = license.ClientName;
            ValidTill = license.ValidTill;
            IsLicenseValid = true;
            return true;
        }
        catch
        {
            return false;
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

    public static string GetMachineId()
    {
        try
        {
            string cpuId = string.Empty;
            using (var mc = new ManagementClass("Win32_Processor"))
            {
                foreach (var mo in mc.GetInstances())
                {
                    cpuId = mo.Properties["ProcessorId"]?.Value?.ToString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(cpuId)) break;
                }
            }

            string mbSerial = string.Empty;
            using (var mc = new ManagementClass("Win32_BaseBoard"))
            {
                foreach (var mo in mc.GetInstances())
                {
                    mbSerial = mo.Properties["SerialNumber"]?.Value?.ToString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(mbSerial)) break;
                }
            }

            var raw = $"{cpuId}_{mbSerial}".Trim();
            if (string.IsNullOrWhiteSpace(raw) || raw == "_")
                raw = Environment.MachineName;

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return BitConverter.ToString(hash).Replace("-", "").Substring(0, 24);
        }
        catch
        {
            return Environment.MachineName;
        }
    }
}