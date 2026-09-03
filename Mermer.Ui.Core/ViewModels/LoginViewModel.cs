// Добавленные using для конфигуратора и настроек
using Mermer.Activations.Services;
using Mermer.Authorization.Services;
using Mermer.Common.Settings;
using Mermer.Enterprise.Models;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.Tools;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using Mermer.Ui.Core.ViewModels.Settings;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels;

public class LoginViewModel : BaseViewModel
{
    private readonly ILoginService _loginService;
    private readonly IBinyatActivationService _activationService;
    private readonly IConfigurator _configurator; // ДОБАВЛЕНО
    private string _username;
    private string _password;

    // ДОБАВЛЕНО IConfigurator configurator в параметры
    public LoginViewModel(
    ILoginService loginService,
    IMvxNavigationService navigationService,
    IBinyatActivationService activationService,
    IUserInteractionService userInteractionService,
    IConfigurator configurator)
    : base(navigationService, userInteractionService)
    {
        this._loginService = loginService;
        this._activationService = activationService;
        this._configurator = configurator;

        // === БЕЗОПАСНОЕ СМЕНА ЯЗЫКА НА САМОМ СТАРТЕ ===
        try
        {
            AppSettings config = configurator.GetConfig<AppSettings>();
            if (config != null && !string.IsNullOrEmpty(config.Culture))
            {
                // Этого достаточно! MvvmCross увидит "ru-RU" и сам пойдет в родной провайдер 
                // искать файл ru-RU.json
                var culture = new System.Globalization.CultureInfo(config.Culture);
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            }
        }
        catch { }
        // ==============================================
    }

    public string Username
    {
        get => this._username;
        set => this.SetProperty<string>(ref this._username, value, nameof(Username));
    }

    public string Password
    {
        get => this._password;
        set => this.SetProperty<string>(ref this._password, value, nameof(Password));
    }

    public ICommand LoginCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.LoginAsync), (Func<bool>)(() => !this.IsBusy && !string.IsNullOrEmpty(this.Username) && !string.IsNullOrEmpty(this.Password)));
        }
    }

    private async Task LoginAsync()
    {
        IsBusy = true;
        try
        {
            // Проверяем отдельно клиентскую и серверную лицензии
            var clientStatus = await _activationService.GetClientActiveDatesAsync();
            var serverStatus = await _activationService.GetServerActiveDatesAsync();

            if (!clientStatus.IsActive && !serverStatus.IsActive)
            {
                UserInteractionService.ShowMessage(
                    "Требуется активация",
                    "Лицензии клиента и сервера не активны. Пожалуйста, активируйте оба модуля для продолжения."
                );
                await NavigationService.Navigate<ActivationViewModel>();
                return;
            }

            if (!clientStatus.IsActive)
            {
                UserInteractionService.ShowMessage(
                    "Лицензия клиента не активна",
                    "Лицензия пользователя (клиента) не активирована или её срок действия истёк."
                );
                await NavigationService.Navigate<ActivationViewModel>();
                return;
            }

            if (!serverStatus.IsActive)
            {
                UserInteractionService.ShowMessage(
                    "Лицензия сервера не активна",
                    "Лицензия сервера не активирована или её срок действия истёк."
                );
                await NavigationService.Navigate<ActivationViewModel>();
                return;
            }

            await _loginService.LoginAsync(Username, Password);
            await NavigationService.Navigate<MainViewModel>();
        }
        catch (InvalidOperationException)
        {
            UserInteractionService.ShowMessage(
                this["Error Logging In", Array.Empty<object>()],
                this["User not exists, or wrong password!", Array.Empty<object>()]
            );
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(
                ex,
                $"{this["Error Logging In!", Array.Empty<object>()]} ({ex.GetType().Name})"
            );
        }
        finally
        {
            IsBusy = false;
        }
    }

    public ICommand ShowSettingsCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.ShowSettingsAsync), (Func<bool>)(() => !this.IsBusy));
        }
    }

    private async Task ShowSettingsAsync()
    {
        await this.NavigationService.Navigate<ConnectionSettingsViewModel>();
    }

    public ICommand ShowActivationCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.OnShowActivationAsync), (Func<bool>)(() => !this.IsBusy));
        }
    }

    public Task OnShowActivationAsync() => this.NavigationService.Navigate<ActivationViewModel>();
}