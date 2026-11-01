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
using MvvmCross.Platform;
using Mermer.CRM.Models;
using Mermer.Ui.Core.Helpers;

#nullable disable
namespace Mermer.Ui.Core.ViewModels;

public class LoginViewModel : BaseViewModel
{
    private readonly ILoginService _loginService;
    private readonly IBinyatActivationService _activationService;
    private readonly IConfigurator _configurator;
    private string _username;
    private string _password;

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

        // Force reset licensing cache on startup/login screen load
        this._activationService.ResetCache();

        try
        {
            AppSettings config = configurator.GetConfig<AppSettings>();
            if (config != null && !string.IsNullOrEmpty(config.Culture))
            {
                var culture = new System.Globalization.CultureInfo(config.Culture);
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            }
        }
        catch { }
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
            return (ICommand)new MvxAsyncCommand(
                new Func<Task>(this.LoginAsync),
                (Func<bool>)(() => !this.IsBusy && !string.IsNullOrEmpty(this.Username) && !string.IsNullOrEmpty(this.Password)));
        }
    }

    private async Task LoginAsync()
    {
        IsBusy = true;
        try
        {
            // 1. Обязательная проверка лицензий перед входом
            var clientStatus = await _activationService.GetClientActiveDatesAsync();
            var serverStatus = await _activationService.GetServerActiveDatesAsync();

            bool isClientActive = clientStatus != null && clientStatus.IsActive;
            bool isServerActive = serverStatus != null && serverStatus.IsActive;

            if (!isClientActive || !isServerActive)
            {
                // Снимаем флаг занятости до показа окон
                IsBusy = false;

                string reason = !isClientActive && !isServerActive
                    ? "Both Client and Server licenses are not activated."
                    : (!isClientActive ? "Client license is not activated." : "Server license is not activated.");

                // Сначала выводим ошибку через исключение
                try
                {
                    throw new InvalidOperationException($"Access denied. {reason} Please activate the application.");
                }
                catch (Exception ex)
                {
                    UserInteractionService.ShowExceptionMessage(ex);
                }

                // ТОЛЬКО ПОСЛЕ закрытия диалога ошибки переходим на окно активации
                await NavigationService.Navigate<ActivationViewModel>();
                return;
            }

            // 2. Авторизация
            await _loginService.LoginAsync(Username, Password);

            // 3. Переход в главное окно
            await NavigationService.Navigate<MainViewModel>();
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

    public ICommand ShowSettingsCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(
                new Func<Task>(this.ShowSettingsAsync),
                (Func<bool>)(() => !this.IsBusy));
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
            return (ICommand)new MvxAsyncCommand(
                new Func<Task>(this.OnShowActivationAsync),
                (Func<bool>)(() => !this.IsBusy));
        }
    }

    public Task OnShowActivationAsync() => this.NavigationService.Navigate<ActivationViewModel>();
}