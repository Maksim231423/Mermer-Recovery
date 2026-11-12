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
            // 1. Проверяем обязательные базовые лицензии (Клиент и Сервер)
            var clientStatusTask = _activationService.GetClientActiveDatesAsync();
            var serverStatusTask = _activationService.GetServerActiveDatesAsync();

            await Task.WhenAll(clientStatusTask, serverStatusTask);

            var clientStatus = await clientStatusTask;
            var serverStatus = await serverStatusTask;

            bool isClientActive = clientStatus != null && clientStatus.IsActive;
            bool isServerActive = serverStatus != null && serverStatus.IsActive;

            // 2. Считываем флаг Enable VDS из конфигуратора или реестра
            var connSettings = await _configurator.GetConfigAsync<ConnectionSettings>();
            bool isVdsEnabled = connSettings?.AllowReporting ?? false;

            if (!isVdsEnabled)
            {
                try
                {
                    using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\MermerCS\Binyat_Postgres\ConnectionSettings");
                    if (regKey != null)
                    {
                        var val = regKey.GetValue("AllowReporting")?.ToString();
                        if (bool.TryParse(val, out bool regBool)) isVdsEnabled = regBool;
                    }
                }
                catch { }
            }

            // 3. Если галочка VDS включена — проверяем лицензию синхронизации
            bool isSyncActive = true;
            if (isVdsEnabled)
            {
                var syncStatus = await _activationService.GetSynchronizerActiveDatesAsync();
                isSyncActive = syncStatus != null && syncStatus.IsActive;
            }

            // 4. Если хотя бы одна из требуемых лицензий не активна — прерываем вход
            if (!isClientActive || !isServerActive || !isSyncActive)
            {
                IsBusy = false;

                var missingList = new System.Collections.Generic.List<string>();
                if (!isClientActive) missingList.Add("Client License");
                if (!isServerActive) missingList.Add("Server License");
                if (isVdsEnabled && !isSyncActive) missingList.Add("Cloud Synchronization License (VDS)");

                string missingText = string.Join("\n• ", missingList);
                string message = isVdsEnabled && !isSyncActive
                    ? $"Access denied. VDS cloud synchronization is enabled in settings, but the required licenses are missing or inactive:\n• {missingText}\n\nPlease activate the Synchronization License or disable VDS in Connection Settings."
                    : $"Access denied. The following required licenses are missing or inactive:\n• {missingText}\n\nPlease activate the application.";

                UserInteractionService.ShowMessage("License Not Found", message);

                await NavigationService.Navigate<ActivationViewModel>();
                return;
            }

            // 5. Авторизация
            await _loginService.LoginAsync(Username, Password);

            // 6. Переход в главное окно
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