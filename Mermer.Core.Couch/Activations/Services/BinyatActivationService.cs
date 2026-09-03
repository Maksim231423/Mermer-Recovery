using Mermer.Activations.Models;
using Mermer.Activations.Services;
using Mermer.Licensing.Client.Models;
using Mermer.Licensing.Client.Services;
using Mermer.Ui.Core.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Core.Couch.Activations.Services;

public class BinyatActivationService : IBinyatActivationService
{
    private readonly IActivationService _activationService;
    private readonly IMachineIdProviderService _machineIdProviderService;

    // Папка для хранения лицензий
    private static readonly string LicenseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Mermer",
        "Licenses"
    );

    public BinyatActivationService(
        IActivationService activationService,
        IMachineIdProviderService machineIdProviderService)
    {
        _activationService = activationService;
        _machineIdProviderService = machineIdProviderService;

        if (!Directory.Exists(LicenseDirectory))
        {
            Directory.CreateDirectory(LicenseDirectory);
        }
    }

    public async Task ActivateClientAsync(string licenseId, string note)
    {
        note = EscapeTooLongNotes(note);
        string machineId = await _machineIdProviderService.GetUniqueIdAsync();
        ActivationResult result = await _activationService.ActivateAsync(
            licenseId, machineId, "55ddc105-8f48-4f78-b214-aea448d2a370", note, new[] { "dc60017b-9b20-46ca-8b2e-646de9965a9e" });
        await StoreActivationResultAsync(machineId, result);
    }

    public async Task ActivateServerAsync(string licenseId, string note)
    {
        note = EscapeTooLongNotes(note);
        string serverId = "local-server-node";
        ActivationResult result = await _activationService.ActivateAsync(
            licenseId, serverId, "55ddc105-8f48-4f78-b214-aea448d2a370", note, new[] { "9a953aa5-2fd9-418d-bcf7-fb5bd7d09553" });
        await StoreActivationResultAsync(serverId, result);
    }

    public async Task ActivateSynchronizerAsync(string licenseId, string note)
    {
        note = EscapeTooLongNotes(note);
        string serverId = "local-server-node";
        ActivationResult result = await _activationService.ActivateAsync(
            licenseId, serverId, "55ddc105-8f48-4f78-b214-aea448d2a370", note, new[] { "6b1495a1-60aa-4420-9c30-94718c121c26" });
        await StoreActivationResultAsync(serverId, result);
    }

    public async Task ReactivateClientAsync()
    {
        string machineId = await _machineIdProviderService.GetUniqueIdAsync();
        ActivationResult result = await _activationService.ReactivateAsync(
            machineId, "55ddc105-8f48-4f78-b214-aea448d2a370", new[] { "dc60017b-9b20-46ca-8b2e-646de9965a9e" });
        await StoreActivationResultAsync(machineId, result);
    }

    public async Task ReactivateServerAsync()
    {
        string serverId = "local-server-node";
        ActivationResult result = await _activationService.ReactivateAsync(
            serverId, "55ddc105-8f48-4f78-b214-aea448d2a370", new[] { "9a953aa5-2fd9-418d-bcf7-fb5bd7d09553" });
        await StoreActivationResultAsync(serverId, result);
    }

    public async Task ReactivateSynchronizerAsync()
    {
        string serverId = "local-server-node";
        ActivationResult result = await _activationService.ReactivateAsync(
            serverId, "55ddc105-8f48-4f78-b214-aea448d2a370", new[] { "6b1495a1-60aa-4420-9c30-94718c121c26" });
        await StoreActivationResultAsync(serverId, result);
    }

    public async Task DeactivateClientAsync()
    {
        string machineId = await _machineIdProviderService.GetUniqueIdAsync();
        try { await _activationService.DeactivateAsync(machineId); } catch { }
        await DeleteActivationResultsAsync(machineId);
    }

    public async Task DeactivateServerAsync()
    {
        string serverId = "local-server-node";
        try { await _activationService.DeactivateAsync(serverId); } catch { }
        await DeleteActivationResultsAsync(serverId);
    }

    public async Task DeactivateSynchronizerAsync()
    {
        string serverId = "local-server-node";
        try { await _activationService.DeactivateAsync(serverId); } catch { }
        await DeleteActivationResultsAsync(serverId);
    }

    public async Task<ActivationStatus> GetClientActiveDatesAsync()
    {
        return await GetActiveDatesAsync(await _machineIdProviderService.GetUniqueIdAsync(), "55ddc105-8f48-4f78-b214-aea448d2a370", "dc60017b-9b20-46ca-8b2e-646de9965a9e");
    }

    public async Task<ActivationStatus> GetServerActiveDatesAsync()
    {
        return await GetActiveDatesAsync("local-server-node", "55ddc105-8f48-4f78-b214-aea448d2a370", "9a953aa5-2fd9-418d-bcf7-fb5bd7d09553");
    }

    public async Task<ActivationStatus> GetSynchronizerActiveDatesAsync()
    {
        return await GetActiveDatesAsync("local-server-node", "55ddc105-8f48-4f78-b214-aea448d2a370", "6b1495a1-60aa-4420-9c30-94718c121c26");
    }

    public async Task<ActivationStatus> GetActiveDatesAsync(string machineId, string applicationId, string applicationModuleId)
    {
        var activationResults = await GetActivationResultsAsync(machineId);
        var dates = _activationService.GetActiveDates(machineId, applicationId, applicationModuleId, activationResults)
            .Select(x => new ActiveDate
            {
                DateValidFrom = x.DateValidFrom,
                DateValidTill = x.DateValidTill
            }).ToList();

        // Сравниваем только дату (без учета времени и сдвига часового пояса)
        var today = DateTime.Today;
        bool isActive = dates.Any(x =>
            x.DateValidFrom.Date <= today.AddDays(1) &&
            (!x.DateValidTill.HasValue || x.DateValidTill.Value.Date >= today));

        return new ActivationStatus
        {
            IsActive = isActive,
            ActiveDates = dates
        };
    } 

    public async Task ValidateClientActivationAsync()
    {
        await ValidateActivationAsync(await _machineIdProviderService.GetUniqueIdAsync(), "55ddc105-8f48-4f78-b214-aea448d2a370", "dc60017b-9b20-46ca-8b2e-646de9965a9e");
    }

    public async Task ValidateServerActivationAsync()
    {
        await ValidateActivationAsync("local-server-node", "55ddc105-8f48-4f78-b214-aea448d2a370", "9a953aa5-2fd9-418d-bcf7-fb5bd7d09553");
    }

    public async Task ValidateSynchronizerActivationAsync()
    {
        await ValidateActivationAsync("local-server-node", "55ddc105-8f48-4f78-b214-aea448d2a370", "6b1495a1-60aa-4420-9c30-94718c121c26");
    }

    private async Task ValidateActivationAsync(string machineId, string applicationId, string applicationModuleId)
    {
        var results = await GetActivationResultsAsync(machineId);
        _activationService.ValidateActivation(machineId, applicationId, applicationModuleId, results);
    }

    private string GetFilePath(string machineId)
    {
        var safeId = string.Join("_", machineId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(LicenseDirectory, $"{safeId}.lic");
    }

    private Task StoreActivationResultAsync(string machineId, ActivationResult result)
    {
        try
        {
            var filePath = GetFilePath(machineId);
            var list = new List<ActivationResultItem>();

            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var existing = JsonConvert.DeserializeObject<List<ActivationResultItem>>(json);
                if (existing != null) list = existing;
            }

            list.RemoveAll(x => x.ApplicationId == result.ApplicationId &&
                                x.ApplicationModuleIds != null &&
                                result.ApplicationModuleIds != null &&
                                x.ApplicationModuleIds.SequenceEqual(result.ApplicationModuleIds));

            list.Add(new ActivationResultItem(result));
            File.WriteAllText(filePath, JsonConvert.SerializeObject(list, Formatting.Indented));
        }
        catch { }

        return Task.CompletedTask;
    }

    private Task<IEnumerable<ActivationResult>> GetActivationResultsAsync(string machineId)
    {
        try
        {
            var filePath = GetFilePath(machineId);
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var items = JsonConvert.DeserializeObject<List<ActivationResultItem>>(json);
                if (items != null && items.Any())
                {
                    var results = items.Select(x => new ActivationResult
                    {
                        MachineId = x.MachineId,
                        ApplicationId = x.ApplicationId,
                        ApplicationModuleIds = x.ApplicationModuleIds,
                        DateValidFrom = x.DateValidFrom,
                        DateValidTill = x.DateValidTill,
                        Signature = x.Signature
                    }).ToList();

                    return Task.FromResult<IEnumerable<ActivationResult>>(results);
                }
            }
        }
        catch { }

        return Task.FromResult<IEnumerable<ActivationResult>>(Array.Empty<ActivationResult>());
    }

    private Task DeleteActivationResultsAsync(string machineId)
    {
        try
        {
            var filePath = GetFilePath(machineId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch { }

        return Task.CompletedTask;
    }

    private static string EscapeTooLongNotes(string note)
    {
        if (!string.IsNullOrEmpty(note) && note.Length > 15)
            return note.Substring(0, 15);
        return note ?? string.Empty;
    }
}