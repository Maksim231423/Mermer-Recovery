using Mermer.Common.Settings;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Plugins.Messenger;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Settings;

public class ConnectionSettingsViewModel : DialogViewModel
{
    private const string DefaultLocalAddress = "http://127.0.0.1:5050";
    private const string DefaultVdsAddress = "http://127.0.0.1:5050";

    private readonly IConfigurator _configurator;
    private ConnectionSettings _config;

    private string _serviceAddress;
    private string _vdsServerUrl;
    private bool _isSyncEnabled;
    private decimal _syncIntervalSeconds = 15m;

    public ConnectionSettingsViewModel(
        IMvxMessenger messenger,
        IConfigurator configurator,
        IMvxNavigationService navigationService,
        IUserInteractionService userInteractionService)
        : base(messenger, navigationService, userInteractionService)
    {
        _configurator = configurator;
    }

    public string ServiceAddress
    {
        get => _serviceAddress;
        set => SetProperty(ref _serviceAddress, value, nameof(ServiceAddress));
    }

    public string VdsServerUrl
    {
        get => _vdsServerUrl;
        set => SetProperty(ref _vdsServerUrl, value, nameof(VdsServerUrl));
    }

    public bool IsSyncEnabled
    {
        get => _isSyncEnabled;
        set
        {
            if (value && !_isSyncEnabled)
            {
                // При попытке включить галочку — проверяем наличие лицензии
                _ = VerifyLicensesBeforeEnableAsync(value);
            }
            else
            {
                SetProperty(ref _isSyncEnabled, value, nameof(IsSyncEnabled));
            }
        }
    }

    public decimal SyncIntervalSeconds
    {
        get => _syncIntervalSeconds;
        set => SetProperty(ref _syncIntervalSeconds, Math.Max(5m, value), nameof(SyncIntervalSeconds));
    }

    private async Task VerifyLicensesBeforeEnableAsync(bool targetValue)
    {
        IsBusy = true;
        try
        {
            var (isServerLicensed, isSyncLicensed, errorMessage) = await CheckLicensesStatusAsync(ServiceAddress);

            if (!isSyncLicensed)
            {
                throw new InvalidOperationException(
                    "Лицензия на облачную синхронизацию (VDS) не найдена или не активна. Активируйте модуль Sync для включения синхронизации.");
            }

            SetProperty(ref _isSyncEnabled, targetValue, nameof(IsSyncEnabled));
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
            SetProperty(ref _isSyncEnabled, false, nameof(IsSyncEnabled));
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected override async Task OnLoad()
    {
        _config = await _configurator.GetConfigAsync<ConnectionSettings>() ?? new ConnectionSettings();

        ServiceAddress = !string.IsNullOrWhiteSpace(_config.ServiceAddress) && !_config.ServiceAddress.Contains("8088")
            ? _config.ServiceAddress
            : DefaultLocalAddress;

        VdsServerUrl = !string.IsNullOrWhiteSpace(_config.DatabaseAddress) && _config.DatabaseAddress.StartsWith("http") && !_config.DatabaseAddress.Contains("8091")
            ? _config.DatabaseAddress
            : DefaultVdsAddress;

        // Дефолтно VDS отключен
        _isSyncEnabled = _config.AllowReporting;
        _syncIntervalSeconds = _config.SyncIntervalSeconds > 0 ? (decimal)_config.SyncIntervalSeconds : 15m;

        RaisePropertyChanged(nameof(IsSyncEnabled));
        RaisePropertyChanged(nameof(SyncIntervalSeconds));
    }

    public ICommand TestLocalConnectionCommand => new MvxAsyncCommand(OnTestLocalConnectionAsync, () => !IsBusy);

    private async Task OnTestLocalConnectionAsync()
    {
        await TestUrlConnectivityAsync(ServiceAddress, "Local Database API");
    }

    public ICommand TestVdsConnectionCommand => new MvxAsyncCommand(OnTestVdsConnectionAsync, () => !IsBusy);

    private async Task OnTestVdsConnectionAsync()
    {
        if (!IsSyncEnabled)
        {
            UserInteractionService.ShowMessage("VDS Disabled", "Cloud VDS synchronization is disabled in settings.");
            return;
        }

        await TestUrlConnectivityAsync(VdsServerUrl, "Cloud VDS Server");
    }

    private async Task<(bool isServerActive, bool isSyncActive, string message)> CheckLicensesStatusAsync(string apiBaseUrl)
    {
        string localApi = NormalizeUrl(string.IsNullOrWhiteSpace(apiBaseUrl) ? DefaultLocalAddress : apiBaseUrl);

        using var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        try
        {
            var resp = await client.GetAsync($"{localApi}/api/licensing/status");
            if (!resp.IsSuccessStatusCode)
            {
                return (false, false, $"License service responded with HTTP {(int)resp.StatusCode}.");
            }

            var content = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            bool serverActive = false;
            bool syncActive = false;
            string message = null;

            if (root.TryGetProperty("isServerLicensed", out var pServerLicensed))
                serverActive = pServerLicensed.GetBoolean();
            else if (root.TryGetProperty("isServerActive", out var pServerActive))
                serverActive = pServerActive.GetBoolean();

            if (root.TryGetProperty("isSyncLicensed", out var pSyncLicensed))
                syncActive = pSyncLicensed.GetBoolean();
            else if (root.TryGetProperty("isSyncActive", out var pSyncActive))
                syncActive = pSyncActive.GetBoolean();

            if (root.TryGetProperty("message", out var pMsg))
                message = pMsg.GetString();

            return (serverActive, syncActive, message);
        }
        catch (Exception ex)
        {
            return (false, false, ex.Message);
        }
    }

    private async Task TestUrlConnectivityAsync(string rawUrl, string targetName)
    {
        IsBusy = true;
        try
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                throw new InvalidOperationException($"Please provide a valid address for {targetName}.");
            }

            string testUrl = NormalizeUrl(rawUrl);

            using var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };

            var response = await client.GetAsync($"{testUrl}/api/health");
            if (response.IsSuccessStatusCode)
            {
                UserInteractionService.ShowMessage(
                    "Connection Successful",
                    $"Successfully connected to {targetName} at: {testUrl}"
                );
            }
            else
            {
                throw new HttpRequestException($"Server responded with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(
                new InvalidOperationException($"Could not reach {targetName}: {ex.Message}", ex)
            );
        }
        finally
        {
            IsBusy = false;
        }
    }

    public ICommand SaveCommand => new MvxAsyncCommand(OnSaveAsync, () => !IsBusy);

    private async Task OnSaveAsync()
    {
        IsBusy = true;
        try
        {
            if (string.IsNullOrWhiteSpace(ServiceAddress))
            {
                throw new InvalidOperationException("Local Database API address cannot be empty.");
            }

            // БЛОКИРУЕМ ТОЛЬКО ЕСЛИ ПОЛЬЗОВАТЕЛЬ ПЫТАЕТСЯ ВКЛЮЧИТЬ СИНХРОНИЗАЦИЮ БЕЗ ЛИЦЕНЗИИ
            if (IsSyncEnabled)
            {
                var (isServerLicensed, isSyncLicensed, errorMessage) = await CheckLicensesStatusAsync(ServiceAddress);
                if (!isSyncLicensed)
                {
                    throw new InvalidOperationException(
                        "Лицензия на облачную синхронизацию (VDS) не найдена. Отключите галочку синхронизации или активируйте ключ Sync.");
                }
            }

            _config.ServiceAddress = NormalizeUrl(ServiceAddress);
            _config.DatabaseAddress = NormalizeUrl(VdsServerUrl ?? DefaultVdsAddress);
            _config.AllowReporting = IsSyncEnabled;
            _config.SyncIntervalSeconds = (int)SyncIntervalSeconds;

            // 1. Сохранение в Configurator
            await _configurator.SetConfigAsync(_config);

            // 2. Сохранение в реестр Windows Binyat_Postgres и очистка старого Binyat
            try
            {
                using var parentKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\MermerCS\Binyat_Postgres");
                if (parentKey != null)
                {
                    using var cleanKey = parentKey.CreateSubKey("ConnectionSettings");
                    cleanKey.SetValue("ServiceAddress", _config.ServiceAddress);
                    cleanKey.SetValue("DatabaseAddress", _config.DatabaseAddress);
                    cleanKey.SetValue("AllowReporting", _config.AllowReporting.ToString());
                    cleanKey.SetValue("SyncIntervalSeconds", _config.SyncIntervalSeconds.ToString());
                }

                // Удаляем старый ключ Binyat, чтобы он больше не сбивал порты
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\MermerCS\Binyat", false);
            }
            catch { }

            // 3. Отправка конфигурации в API по HTTP
            await SendSyncConfigToApiAsync(_config.ServiceAddress, _config.DatabaseAddress, _config.AllowReporting, _config.SyncIntervalSeconds);

            UserInteractionService.ShowMessage(
                "Settings Saved",
                "Connection & VDS synchronization settings have been successfully applied."
            );

            await OnCloseAsync();
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SendSyncConfigToApiAsync(string apiBaseUrl, string serverUrl, bool enabled, int intervalSeconds)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var payload = new
            {
                Enabled = enabled,
                ServerUrl = serverUrl,
                SyncIntervalSeconds = intervalSeconds
            };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await client.PostAsync($"{apiBaseUrl}/api/sync/config", content);
        }
        catch { }
    }

    private static string NormalizeUrl(string url)
    {
        string trimmed = (url ?? string.Empty).Trim().TrimEnd('/');
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "http://" + trimmed;
        }
        return trimmed;
    }
}