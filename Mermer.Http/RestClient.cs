using Newtonsoft.Json;
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

        // Читаем поток без захвата контекста UI
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var sr = new System.IO.StreamReader(stream);
        using var reader = new JsonTextReader(sr);

        // Парсинг выполняется строго в пуле фоновых потоков (Task.Run), не фризя интерфейс!
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
            body = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var restEx = JsonConvert.DeserializeObject<RestException>(body, RestClient.JsonSerializerSettings);
                    if (restEx != null) return restEx.ToExecption();
                }
                catch { }
            }
        }
        catch { }

        var message = $"[HTTP {(int)response.StatusCode} {response.ReasonPhrase}] URL: '{address}'. Details: {body}";
        Debug.WriteLine(message);
        return new Exception(message);
    }

    private static StringContent PrepareContent(object model)
    {
        StringContent stringContent = new StringContent(JsonConvert.SerializeObject(model, Formatting.Indented, RestClient.JsonSerializerSettings));
        stringContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        return stringContent;
    }
}