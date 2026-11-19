using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Http;

public class RestClient
{
    protected readonly HttpClient HttpClient;
    private static readonly JsonSerializerSettings JsonSerializerSettings = new JsonSerializerSettings()
    {
        TypeNameHandling = TypeNameHandling.Auto,
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore
    };

    public RestClient(HttpClient httpClient) => this.HttpClient = httpClient;

    public async Task<T> GetAsync<T>(string address)
    {
        try
        {
            var response = await this.HttpClient.GetAsync(address);
            return await RestClient.ExportResult<T>(response, address);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HTTP GET FAILED] {address} -> {ex.Message}");
            throw;
        }
    }

    public async Task PostAsync(string address, object model)
    {
        try
        {
            StringContent content = RestClient.PrepareContent(model);
            var response = await this.HttpClient.PostAsync(address, (HttpContent)content);
            await RestClient.ExportResult(response, address);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HTTP POST FAILED] {address} -> {ex.Message}");
            throw;
        }
    }

    public async Task<T> PostAsync<T>(string address, object model)
    {
        try
        {
            StringContent content = RestClient.PrepareContent(model);
            var response = await this.HttpClient.PostAsync(address, (HttpContent)content);
            return await RestClient.ExportResult<T>(response, address);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HTTP POST<T> FAILED] {address} -> {ex.Message}");
            throw;
        }
    }

    public async Task PutAsync(string address, object model)
    {
        try
        {
            StringContent content = RestClient.PrepareContent(model);
            var response = await this.HttpClient.PutAsync(address, (HttpContent)content);
            await RestClient.ExportResult(response, address);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HTTP PUT FAILED] {address} -> {ex.Message}");
            throw;
        }
    }

    public async Task<T> PutAsync<T>(string address, object model)
    {
        try
        {
            StringContent content = RestClient.PrepareContent(model);
            var response = await this.HttpClient.PutAsync(address, (HttpContent)content);
            return await RestClient.ExportResult<T>(response, address);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HTTP PUT<T> FAILED] {address} -> {ex.Message}");
            throw;
        }
    }

    public async Task DeleteAsync(string endpoint)
    {
        try
        {
            var response = await this.HttpClient.DeleteAsync(endpoint);
            await RestClient.ExportResult(response, endpoint);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HTTP DELETE FAILED] {endpoint} -> {ex.Message}");
            throw;
        }
    }

    private static async Task ExportResult(HttpResponseMessage response, string address = "")
    {
        if (!response.IsSuccessStatusCode)
            throw await RestClient.ExportException(response, address);
    }

    private static async Task<T> ExportResult<T>(HttpResponseMessage response, string address = "")
    {
        if (!response.IsSuccessStatusCode)
            throw await RestClient.ExportException(response, address).ConfigureAwait(false);

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var sr = new System.IO.StreamReader(stream);
        using var reader = new JsonTextReader(sr);

        return await Task.Run(() =>
        {
            var serializer = JsonSerializer.Create(JsonSerializerSettings);
            return serializer.Deserialize<T>(reader);
        }).ConfigureAwait(false);
    }

    private static async Task<Exception> ExportException(HttpResponseMessage response, string address = "")
    {
        string body = "";
        try
        {
            body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(body))
            {
                // 1. Сначала пробуем извлечь message / detail / error из JSON (работает и для Results.Json, и для Results.Problem)
                try
                {
                    var obj = JObject.Parse(body);
                    var extracted =
                        obj.GetValue("message", StringComparison.OrdinalIgnoreCase)?.ToString() ??
                        obj.GetValue("detail", StringComparison.OrdinalIgnoreCase)?.ToString() ??
                        obj.GetValue("error", StringComparison.OrdinalIgnoreCase)?.ToString() ??
                        obj.GetValue("exceptionMessage", StringComparison.OrdinalIgnoreCase)?.ToString();

                    if (!string.IsNullOrWhiteSpace(extracted))
                    {
                        Debug.WriteLine($"[HTTP {(int)response.StatusCode}] URL: '{address}' -> {extracted}");
                        return new Exception(extracted);
                    }
                }
                catch { }

                // 2. Если это стандартный формат RestException с непустым сообщением
                try
                {
                    var restEx = JsonConvert.DeserializeObject<RestException>(body, RestClient.JsonSerializerSettings);
                    var mappedEx = restEx?.ToExecption();
                    if (mappedEx != null &&
                        !string.IsNullOrWhiteSpace(mappedEx.Message) &&
                        !mappedEx.Message.StartsWith("Exception of type", StringComparison.OrdinalIgnoreCase))
                    {
                        return mappedEx;
                    }
                }
                catch { }
            }
        }
        catch { }

        var fallbackMessage = !string.IsNullOrWhiteSpace(body)
            ? body
            : $"[HTTP {(int)response.StatusCode} {response.ReasonPhrase}] URL: '{address}'";

        Debug.WriteLine(fallbackMessage);
        return new Exception(fallbackMessage);
    }

    private static StringContent PrepareContent(object model)
    {
        StringContent stringContent = new StringContent(JsonConvert.SerializeObject(model, Formatting.Indented, RestClient.JsonSerializerSettings));
        stringContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        return stringContent;
    }
}