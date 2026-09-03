// Decompiled with JetBrains decompiler
// Type: Mermer.Licensing.Client.Rest.ActivationService
// Assembly: Mermer.Licensing.Client, Version=0.0.6.0, Culture=neutral, PublicKeyToken=null
// MVID: D27E04BC-87FA-488D-A2D4-54F1C56BAB05
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Licensing.Client.dll

using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Mermer.Licensing.Client.Models;
using Mermer.Licensing.Client.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Licensing.Client.Rest;

public class ActivationService : IActivationService, IDisposable
{
  private readonly ICryptoService _cryptoService;
  private readonly ActivationConfiguration _config;
  private readonly HttpClient _client;

  public ActivationService(
    ICryptoService cryptoService,
    IOptions<ActivationConfiguration> configOptions)
  {
    this._config = configOptions.Value;
    this._cryptoService = cryptoService;
    this._client = new HttpClient()
    {
      BaseAddress = new Uri(this._config.ActivationUrl)
    };
  }

  public void ValidateActivation(
    string machineId,
    string applicationId,
    string applicationModuleId,
    IEnumerable<ActivationResult> activations)
  {
    if (!this.IsActivated(machineId, applicationId, applicationModuleId, activations))
      throw new Exception();
  }

  public bool IsActivated(
    string machineId,
    string applicationId,
    string applicationModuleId,
    IEnumerable<ActivationResult> activations)
  {
    return this.GetActiveDates(machineId, applicationId, applicationModuleId, activations).Any<(DateTime, DateTime?)>((Func<(DateTime, DateTime?), bool>) (x =>
    {
      if (!(x.Item1 <= DateTime.Today))
        return false;
      if (!x.Item2.HasValue)
        return true;
      DateTime? dateValidTill = x.Item2;
      DateTime today = DateTime.Today;
      return dateValidTill.HasValue && dateValidTill.GetValueOrDefault() >= today;
    }));
  }

  public IEnumerable<(DateTime DateValidFrom, DateTime? DateValidTill)> GetActiveDates(
    string machineId,
    string applicationId,
    string applicationModuleId,
    IEnumerable<ActivationResult> activations)
  {
    return activations.Where<ActivationResult>(new Func<ActivationResult, bool>(this.IsValidResult)).Where<ActivationResult>((Func<ActivationResult, bool>) (x => x.MachineId == machineId && x.ApplicationId == applicationId && ((IEnumerable<string>) x.ApplicationModuleIds).Contains<string>(applicationModuleId))).Select<ActivationResult, (DateTime, DateTime?)>((Func<ActivationResult, (DateTime, DateTime?)>) (x => (x.DateValidFrom, x.DateValidTill)));
  }

    public async Task<ActivationResult> ActivateAsync(
      string licenseId,
      string machineId,
      string applicationId,
      string note,
      string[] applicationModuleIds)
    {
        // Явно указываем относительный путь от корня API
        ActivationResult result = await this.ExportResultAsync<ActivationResult>(
            await this._client.PostAsync("/api/licensing/activate", (HttpContent)this.PrepareContent((object)new ActivationRequest()
            {
                LicenseId = licenseId,
                MachineId = machineId,
                Note = note,
                ApplicationId = applicationId,
                ApplicationModuleIds = applicationModuleIds
            })));
        this.ValidateResult(result);
        return result;
    }

    public async Task<ActivationResult> ActivateTrialAsync(
    string machineId,
    string applicationId,
    string[] applicationModuleIds)
  {
    ActivationResult result = await this.ExportResultAsync<ActivationResult>(await this._client.PostAsync("activate/trial", (HttpContent) this.PrepareContent((object) new ActivationRequest()
    {
      MachineId = machineId,
      ApplicationId = applicationId,
      ApplicationModuleIds = applicationModuleIds
    })));
    this.ValidateResult(result);
    return result;
  }

    public async Task<ActivationResult> ReactivateAsync(
      string machineId,
      string applicationId,
      string[] applicationModuleIds)
    {
        ActivationResult result = await this.ExportResultAsync<ActivationResult>(
            await this._client.PostAsync("/api/licensing/reactivate", (HttpContent)this.PrepareContent((object)new ReactivationRequest()
            {
                MachineId = machineId,
                ApplicationId = applicationId,
                ApplicationModuleIds = applicationModuleIds
            })));
        this.ValidateResult(result);
        return result;
    }

    public async Task DeactivateAsync(string machineId)
    {
        HttpResponseMessage response = await this._client.PostAsync("/api/licensing/deactivate", (HttpContent)this.PrepareContent((object)new DeactivationRequest()
        {
            MachineId = machineId
        }));
        if (!response.IsSuccessStatusCode)
            throw await ActivationService.ExportException(response);
    }

    private void ValidateResult(ActivationResult result)
    {
        if (result == null)
            throw new Exception("Не удалось получить результат активации от сервера.");
    }

    private bool IsValidResult(ActivationResult result)
    {
        // Отключаем проверку криптографической подписи старого сервера Binyat
        return result != null;
    }

    private StringContent PrepareContent(object model)
    {
        // Отправляем открытый JSON напрямую без обертки в RSA body
        var json = JsonConvert.SerializeObject(model);
        var stringContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return stringContent;
    }

    private async Task<T> ExportResultAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw await ExportException(response);

        string str = await response.Content.ReadAsStringAsync();
        return !string.IsNullOrEmpty(str) ? JsonConvert.DeserializeObject<T>(str) : default(T);
    }

    private static async Task<Exception> ExportException(HttpResponseMessage response)
    {
        string message = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(message))
            return new Exception($"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})");
        return new Exception($"HTTP {(int)response.StatusCode}: {message}");
    }

    public void Dispose() => this._client?.Dispose();
}
