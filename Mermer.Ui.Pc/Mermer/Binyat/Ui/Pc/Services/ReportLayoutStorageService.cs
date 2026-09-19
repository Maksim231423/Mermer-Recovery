using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Mermer.Ui.Pc.Services;

public class ReportLayoutStorageService : IReportLayoutStorageService
{
    private readonly HttpClient _httpClient;
    private readonly string _localReportsFolder;

    public ReportLayoutStorageService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:5050/"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        _localReportsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mermer",
            "Reports"
        );

        if (!Directory.Exists(_localReportsFolder))
        {
            Directory.CreateDirectory(_localReportsFolder);
        }
    }

    public async Task<string> GetAsync(string reportName)
    {
        string localFile = Path.Combine(_localReportsFolder, string.Format("{0}.xml", reportName));

        try
        {
            var response = await _httpClient.GetAsync(string.Format("api/reports/layout/{0}", reportName));
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeAnonymousType(json, new { Layout = "" });
                if (result != null && !string.IsNullOrEmpty(result.Layout))
                {
                    await WriteTextToFileAsync(localFile, result.Layout);
                    return result.Layout;
                }
            }
        }
        catch
        {
            // Фоллбэк на локальный кэш
        }

        if (File.Exists(localFile))
        {
            return await ReadTextFromFileAsync(localFile);
        }

        return null;
    }

    public async Task StoreAsync(string reportName, string reportLayout)
    {
        string localFile = Path.Combine(_localReportsFolder, string.Format("{0}.xml", reportName));

        await WriteTextToFileAsync(localFile, reportLayout);

        try
        {
            var payload = new { Name = reportName, Layout = reportLayout };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            await _httpClient.PostAsync("api/reports/layout", content);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(string.Format("[ReportStorage] Ошибка отправки отчета на сервер: {0}", ex.Message));
        }
    }

    private static async Task WriteTextToFileAsync(string filePath, string content)
    {
        using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            await writer.WriteAsync(content);
        }
    }

    private static async Task<string> ReadTextFromFileAsync(string filePath)
    {
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            return await reader.ReadToEndAsync();
        }
    }
}