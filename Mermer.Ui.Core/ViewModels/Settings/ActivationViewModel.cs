using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Plugins.Messenger;
using Mermer.Activations.Models;
using Mermer.Activations.Services;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Settings;

public class ActivationViewModel : DialogViewModel
{
    private readonly IBinyatActivationService _activationService;
    private string _note;
    private string _clientLicenseId;
    private ActivationStatus _clientActivationStatus;
    private string _serverLicenseId;
    private ActivationStatus _serverActivationStatus;

    public ActivationViewModel(
        IMvxMessenger messenger,
        IMvxNavigationService navigationService,
        IBinyatActivationService activationService,
        IUserInteractionService userInteractionService)
        : base(messenger, navigationService, userInteractionService)
    {
        _activationService = activationService;
    }

    protected override async Task OnLoad()
    {
        await base.OnLoad();
        await Task.WhenAll(OnUpdateClientStatusAsync(), OnUpdateServerStatusAsync());
    }

    public virtual string Note
    {
        get => _note;
        set => SetProperty(ref _note, value, nameof(Note));
    }

    // ==========================================
    // CLIENT LICENSE
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
            UserInteractionService.ShowMessage("Успешно", "Лицензия клиента успешно активирована!");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Ошибка активации", CleanErrorMessage(ex.Message));
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
            UserInteractionService.ShowMessage("Успешно", "Лицензия клиента успешно обновлена!");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Ошибка реактивации", CleanErrorMessage(ex.Message));
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
            UserInteractionService.ShowMessage("Успешно", "Лицензия клиента деактивирована.");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Ошибка деактивации", CleanErrorMessage(ex.Message));
        }
        finally
        {
            IsBusy = false;
            UpdateClientStatusCommand.Execute(null);
        }
    }

    // ==========================================
    // SERVER LICENSE
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
            UserInteractionService.ShowMessage("Успешно", "Лицензия сервера успешно активирована!");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Ошибка активации", CleanErrorMessage(ex.Message));
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
            UserInteractionService.ShowMessage("Успешно", "Лицензия сервера успешно обновлена!");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Ошибка реактивации", CleanErrorMessage(ex.Message));
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
            UserInteractionService.ShowMessage("Успешно", "Лицензия сервера деактивирована.");
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowMessage("Ошибка деактивации", CleanErrorMessage(ex.Message));
        }
        finally
        {
            IsBusy = false;
            UpdateServerStatusCommand.Execute(null);
        }
    }

    // ==========================================
    // HELPERS
    // ==========================================

    private static string CleanErrorMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return "Не удалось активировать лицензию. Проверьте правильность введённого ключа.";

        var message = rawMessage;
        var colonIndex = message.IndexOf(':');
        if (colonIndex >= 0 && message.StartsWith("HTTP", StringComparison.OrdinalIgnoreCase))
        {
            message = message.Substring(colonIndex + 1);
        }

        return message.Trim(' ', '"', '\r', '\n');
    }
}