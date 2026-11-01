using Mermer.Common.Settings;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Platform;
using MvvmCross.Plugins.Messenger;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Settings;

public class ConnectionSettingsViewModel : DialogViewModel
{
    private const string DefaultLocalAddress = "http://127.0.0.1:5050";
    private const string DefaultVdsAddress = "https://vds.mermer-erp.com:5050";

    private readonly IConfigurator _configurator;
    private ConnectionSettings _config;

    private string _serviceAddress;
    private string _vdsServerUrl;
    private bool _isSyncEnabled;

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
                // При попытке включить синхронизацию сразу верифицируем серверную лицензию
                _ = VerifyServerLicenseBeforeEnableAsync(value);
            }
            else
            {
                SetProperty(ref _isSyncEnabled, value, nameof(IsSyncEnabled));
            }
        }
    }

    private async Task VerifyServerLicenseBeforeEnableAsync(bool targetValue)
    {
        IsBusy = true;
        try
        {
            bool isServerLicensed = await CheckServerLicenseStatusAsync(ServiceAddress);
            if (!isServerLicensed)
            {
                UserInteractionService.ShowMessage(
                    "License Required",
                    "Cloud VDS Synchronization requires an active Server License. Please activate your server license first."
                );
                SetProperty(ref _isSyncEnabled, false, nameof(IsSyncEnabled));
                return;
            }

            SetProperty(ref _isSyncEnabled, targetValue, nameof(IsSyncEnabled));
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage(
                "License Verification Failed",
                $"Could not verify server license: {ex.Message}"
            );
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

        ServiceAddress = !string.IsNullOrWhiteSpace(_config.ServiceAddress)
            ? _config.ServiceAddress
            : DefaultLocalAddress;

        VdsServerUrl = !string.IsNullOrWhiteSpace(_config.DatabaseAddress) && _config.DatabaseAddress.StartsWith("http")
            ? _config.DatabaseAddress
            : DefaultVdsAddress;

        _isSyncEnabled = _config.AllowReporting;
        RaisePropertyChanged(nameof(IsSyncEnabled));
    }

    public ICommand TestLocalConnectionCommand => new MvxAsyncCommand(OnTestLocalConnectionAsync, () => !IsBusy);

    private async Task OnTestLocalConnectionAsync()
    {
        await TestUrlConnectivityAsync(ServiceAddress, "Local Database API");
    }

    public ICommand TestVdsConnectionCommand => new MvxAsyncCommand(OnTestVdsConnectionAsync, () => !IsBusy);

    private async Task OnTestVdsConnectionAsync()
    {
        // 1. Проверяем наличие серверной лицензии перед тестом VDS
        IsBusy = true;
        try
        {
            bool isLicensed = await CheckServerLicenseStatusAsync(ServiceAddress);
            if (!isLicensed)
            {
                throw new InvalidOperationException("Active Server License was not found. VDS connection is prohibited without a valid license.");
            }
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
            IsBusy = false;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        // 2. Если лицензия валидна — тестируем соединение с VDS
        await TestUrlConnectivityAsync(VdsServerUrl, "Cloud VDS Server");
    }

    private async Task<bool> CheckServerLicenseStatusAsync(string apiBaseUrl)
    {
        string localApi = NormalizeUrl(string.IsNullOrWhiteSpace(apiBaseUrl) ? DefaultLocalAddress : apiBaseUrl);

        using var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        // Стучимся в эндпоинт проверки серверной лицензии
        var resp = await client.GetAsync($"{localApi}/api/licensing/status");
        if (!resp.IsSuccessStatusCode)
        {
            return false;
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        if (doc.RootElement.TryGetProperty("isServerActive", out var prop))
        {
            return prop.GetBoolean();
        }

        if (doc.RootElement.TryGetProperty("isActive", out var activeProp))
        {
            return activeProp.GetBoolean();
        }

        return false;
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
            // ВРЕМЕННАЯ ДИАГНОСТИКА: покажет реальный тип конфигуратора
            var configuratorType = _configurator.GetType().FullName;
            System.Diagnostics.Debug.WriteLine($"[CONFIGURATOR TYPE]: {configuratorType}");

            if (string.IsNullOrWhiteSpace(ServiceAddress))
            {
                throw new InvalidOperationException("Local Database API address cannot be empty.");
            }

            // Если пользователь сохраняет включенную синхронизацию — контрольная проверка лицензии
            if (IsSyncEnabled)
            {
                bool isLicensed = await CheckServerLicenseStatusAsync(ServiceAddress);
                if (!isLicensed)
                {
                    throw new InvalidOperationException("Cannot enable Cloud Synchronization without an active Server License.");
                }
            }

            _config.ServiceAddress = NormalizeUrl(ServiceAddress);
            _config.DatabaseAddress = NormalizeUrl(VdsServerUrl ?? DefaultVdsAddress);
            _config.AllowReporting = IsSyncEnabled;

            // Сохранение
            await _configurator.SetConfigAsync(_config);

            // Полная пересоздание ветки с удалением всех устаревших полей
            using (var parentKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\MermerCS\Binyat_Postgres", true))
            {
                if (parentKey != null)
                {
                    // 1. Стираем старый подраздел со всеми лишними полями (Couchbase/прямой Postgres)
                    parentKey.DeleteSubKeyTree("ConnectionSettings", false);

                    // 2. Создаем его заново с нуля
                    using (var cleanKey = parentKey.CreateSubKey("ConnectionSettings"))
                    {
                        cleanKey.SetValue("ServiceAddress", _config.ServiceAddress);
                        cleanKey.SetValue("DatabaseAddress", _config.DatabaseAddress);
                        cleanKey.SetValue("AllowReporting", _config.AllowReporting.ToString());
                    }
                }
            }

            UserInteractionService.ShowMessage(
                "Settings Saved",
                "Connection & VDS synchronization settings have been successfully saved."
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