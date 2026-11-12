using Mermer.Activations.Models;
using Mermer.Activations.Services;
using Mermer.Common.Settings;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Plugins.Messenger;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Settings;

public class ActivationViewModel : DialogViewModel
{
    private readonly IBinyatActivationService _activationService;
    private readonly IConfigurator _configurator;

    private string _note;
    private string _clientLicenseId;
    private ActivationStatus _clientActivationStatus;

    private string _serverLicenseId;
    private ActivationStatus _serverActivationStatus;

    private string _syncLicenseId;
    private ActivationStatus _syncActivationStatus;

    private bool _isVdsEnabled;
    private string _machineId;

    public bool IsVdsEnabled
    {
        get => _isVdsEnabled;
        set => SetProperty(ref _isVdsEnabled, value, nameof(IsVdsEnabled));
    }

    public string MachineId
    {
        get => _machineId;
        set => SetProperty(ref _machineId, value, nameof(MachineId));
    }

    public ActivationViewModel(
        IMvxMessenger messenger,
        IMvxNavigationService navigationService,
        IBinyatActivationService activationService,
        IUserInteractionService userInteractionService,
        IConfigurator configurator)
        : base(messenger, navigationService, userInteractionService)
    {
        _activationService = activationService ?? throw new Exception("Activation service cannot be null.");
        _configurator = configurator;
    }

    protected override async Task OnLoad()
    {
        await base.OnLoad();

        // 1. Считываем флаг Enable VDS из конфигуратора или системного реестра
        try
        {
            var connSettings = await _configurator.GetConfigAsync<ConnectionSettings>();
            IsVdsEnabled = connSettings?.AllowReporting ?? false;

            if (!IsVdsEnabled)
            {
                using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\MermerCS\Binyat_Postgres\ConnectionSettings");
                if (regKey != null)
                {
                    var val = regKey.GetValue("AllowReporting")?.ToString();
                    if (bool.TryParse(val, out bool regBool)) IsVdsEnabled = regBool;
                }
            }
        }
        catch
        {
            IsVdsEnabled = false;
        }

        // 2. Получаем идентификатор оборудования
        try
        {
            MachineId = GetHardwareMachineId();
            Note = $"{Environment.MachineName} - {Environment.UserName}";
        }
        catch
        {
            MachineId = Environment.MachineName;
            Note = MachineId;
        }

        // 3. Загружаем статусы лицензий
        var tasks = new System.Collections.Generic.List<Task>
        {
            OnUpdateClientStatusAsync(),
            OnUpdateServerStatusAsync()
        };

        if (IsVdsEnabled)
        {
            tasks.Add(OnUpdateSyncStatusAsync());
        }

        await Task.WhenAll(tasks);
    }

    private static string GetHardwareMachineId()
    {
        try
        {
            var raw = string.Format("{0}_{1}", Environment.MachineName, Environment.ProcessorCount);
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
            return BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, 24);
        }
        catch
        {
            return Environment.MachineName;
        }
    }

    public virtual string Note
    {
        get => _note;
        set => SetProperty(ref _note, value, nameof(Note));
    }

    // ==========================================
    // 1. CLIENT LICENSE
    // ==========================================

    public virtual string ClientLicenseId
    {
        get => _clientLicenseId;
        set => SetProperty(ref _clientLicenseId, value, nameof(ClientLicenseId));
    }

    public ActivationStatus ClientActivationStatus
    {
        get => _clientActivationStatus;
        set => SetProperty(ref _clientActivationStatus, value, nameof(ClientActivationStatus));
    }

    public ICommand UpdateClientStatusCommand =>
        new MvxAsyncCommand(OnUpdateClientStatusAsync, () => !IsBusy);

    private async Task OnUpdateClientStatusAsync()
    {
        IsBusy = true;
        try
        {
            ClientActivationStatus = await _activationService.GetClientActiveDatesAsync();
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

    public ICommand ActivateClientCommand =>
        new MvxAsyncCommand(OnActivateClientAsync, () => !IsBusy && !string.IsNullOrEmpty(ClientLicenseId));

    public virtual async Task OnActivateClientAsync()
    {
        IsBusy = true;
        try
        {
            await _activationService.ActivateClientAsync(ClientLicenseId, Note);
            UserInteractionService.ShowMessage("Success", "Client license activated successfully.");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Activation Error", CleanErrorMessage(ex.Message));
        }
        finally
        {
            IsBusy = false;
            UpdateClientStatusCommand.Execute(null);
        }
    }

    public ICommand ReactivateClientCommand =>
        new MvxAsyncCommand(OnReactivateClientAsync, () => !IsBusy);

    private async Task OnReactivateClientAsync()
    {
        IsBusy = true;
        try
        {
            await _activationService.ReactivateClientAsync();
            UserInteractionService.ShowMessage("Success", "Client license renewed successfully.");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Reactivation Error", CleanErrorMessage(ex.Message));
        }
        finally
        {
            IsBusy = false;
            UpdateClientStatusCommand.Execute(null);
        }
    }

    public ICommand DeactivateClientCommand =>
        new MvxAsyncCommand(OnDeactivateClientAsync, () => !IsBusy);

    private async Task OnDeactivateClientAsync()
    {
        IsBusy = true;
        try
        {
            await _activationService.DeactivateClientAsync();
            UserInteractionService.ShowMessage("Success", "Client license deactivated.");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Deactivation Error", CleanErrorMessage(ex.Message));
        }
        finally
        {
            IsBusy = false;
            UpdateClientStatusCommand.Execute(null);
        }
    }

    // ==========================================
    // 2. SERVER LICENSE
    // ==========================================

    public string ServerLicenseId
    {
        get => _serverLicenseId;
        set => SetProperty(ref _serverLicenseId, value, nameof(ServerLicenseId));
    }

    public ActivationStatus ServerActivationStatus
    {
        get => _serverActivationStatus;
        set => SetProperty(ref _serverActivationStatus, value, nameof(ServerActivationStatus));
    }

    public ICommand UpdateServerStatusCommand =>
        new MvxAsyncCommand(OnUpdateServerStatusAsync, () => !IsBusy);

    private async Task OnUpdateServerStatusAsync()
    {
        IsBusy = true;
        try
        {
            ServerActivationStatus = await _activationService.GetServerActiveDatesAsync();
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

    public ICommand ActivateServerCommand =>
        new MvxAsyncCommand(OnActivateServerAsync, () => !IsBusy && !string.IsNullOrEmpty(ServerLicenseId));

    public virtual async Task OnActivateServerAsync()
    {
        IsBusy = true;
        try
        {
            await _activationService.ActivateServerAsync(ServerLicenseId, Note);
            UserInteractionService.ShowMessage("Success", "Server license activated successfully.");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Activation Error", CleanErrorMessage(ex.Message));
        }
        finally
        {
            IsBusy = false;
            UpdateServerStatusCommand.Execute(null);
        }
    }

    public ICommand ReactivateServerCommand =>
        new MvxAsyncCommand(OnReactivateServerAsync, () => !IsBusy);

    private async Task OnReactivateServerAsync()
    {
        IsBusy = true;
        try
        {
            await _activationService.ReactivateServerAsync();
            UserInteractionService.ShowMessage("Success", "Server license renewed successfully.");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Reactivation Error", CleanErrorMessage(ex.Message));
        }
        finally
        {
            IsBusy = false;
            UpdateServerStatusCommand.Execute(null);
        }
    }

    public ICommand DeactivateServerCommand =>
        new MvxAsyncCommand(OnDeactivateServerAsync, () => !IsBusy);

    private async Task OnDeactivateServerAsync()
    {
        IsBusy = true;
        try
        {
            await _activationService.DeactivateServerAsync();
            UserInteractionService.ShowMessage("Success", "Server license deactivated.");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Deactivation Error", CleanErrorMessage(ex.Message));
        }
        finally
        {
            IsBusy = false;
            UpdateServerStatusCommand.Execute(null);
        }
    }

    // ==========================================
    // 3. SYNCHRONIZATION LICENSE
    // ==========================================

    public string SyncLicenseId
    {
        get => _syncLicenseId;
        set => SetProperty(ref _syncLicenseId, value, nameof(SyncLicenseId));
    }

    public ActivationStatus SyncActivationStatus
    {
        get => _syncActivationStatus;
        set => SetProperty(ref _syncActivationStatus, value, nameof(SyncActivationStatus));
    }

    public ICommand UpdateSyncStatusCommand =>
        new MvxAsyncCommand(OnUpdateSyncStatusAsync, () => !IsBusy && IsVdsEnabled);

    private async Task OnUpdateSyncStatusAsync()
    {
        if (!IsVdsEnabled) return;

        IsBusy = true;
        try
        {
            SyncActivationStatus = await _activationService.GetSynchronizerActiveDatesAsync();
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

    public ICommand ActivateSyncCommand =>
        new MvxAsyncCommand(OnActivateSyncAsync, () => !IsBusy && IsVdsEnabled && !string.IsNullOrEmpty(SyncLicenseId));

    public virtual async Task OnActivateSyncAsync()
    {
        if (!IsVdsEnabled) return;

        IsBusy = true;
        try
        {
            await _activationService.ActivateSynchronizerAsync(SyncLicenseId, Note);
            UserInteractionService.ShowMessage("Success", "Synchronization license activated successfully.");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Activation Error", CleanErrorMessage(ex.Message));
        }
        finally
        {
            IsBusy = false;
            UpdateSyncStatusCommand.Execute(null);
        }
    }

    public ICommand ReactivateSyncCommand =>
        new MvxAsyncCommand(OnReactivateSyncAsync, () => !IsBusy && IsVdsEnabled);

    private async Task OnReactivateSyncAsync()
    {
        if (!IsVdsEnabled) return;

        IsBusy = true;
        try
        {
            await _activationService.ReactivateSynchronizerAsync();
            UserInteractionService.ShowMessage("Success", "Synchronization license renewed successfully.");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Reactivation Error", CleanErrorMessage(ex.Message));
        }
        finally
        {
            IsBusy = false;
            UpdateSyncStatusCommand.Execute(null);
        }
    }

    public ICommand DeactivateSyncCommand =>
        new MvxAsyncCommand(OnDeactivateSyncAsync, () => !IsBusy && IsVdsEnabled);

    private async Task OnDeactivateSyncAsync()
    {
        if (!IsVdsEnabled) return;

        IsBusy = true;
        try
        {
            await _activationService.DeactivateSynchronizerAsync();
            UserInteractionService.ShowMessage("Success", "Synchronization license deactivated.");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Deactivation Error", CleanErrorMessage(ex.Message));
        }
        finally
        {
            IsBusy = false;
            UpdateSyncStatusCommand.Execute(null);
        }
    }

    private static string CleanErrorMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return "Failed to activate license. Please verify the provided license key.";

        var message = rawMessage;
        var colonIndex = message.IndexOf(':');
        if (colonIndex >= 0 && message.StartsWith("HTTP", StringComparison.OrdinalIgnoreCase))
        {
            message = message.Substring(colonIndex + 1);
        }

        return message.Trim(' ', '"', '\r', '\n');
    }
}