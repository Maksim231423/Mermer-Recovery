using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Commerce.Models;
using Mermer.Commerce.Services;
using Mermer.CRM.Models;
using Mermer.Data.Authorizers;
using Mermer.Enterprise.Models;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Plugins.Messenger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Commerce;

public class InvoicesListViewModel : ListViewModelBaseWithFilterDate<InvoiceInfo>
{
    private readonly IInvoicesRepository _repository;
    private readonly IListAuthorizer<Invoice> _authorizer;
    private readonly ILoginService _loginService;

    public InvoicesListViewModel(
          IMvxMessenger messenger,
          Reference<Office> offices,
          Reference<Partner> partners,
          Reference<Warehouse> warehouses,
          Reference<Depository> depositories,
          IInvoicesRepository repository,
          IListAuthorizer<Invoice> authorizer,
          IMvxNavigationService navigationService,
          IUserInteractionService userInteractionService,
          ILoginService loginService)
          : base(messenger, navigationService, userInteractionService)
    {
        this._repository = repository;
        this._authorizer = authorizer;
        this._loginService = loginService;
        this.Offices = offices;
        this.Partners = partners;
        this.Warehouses = warehouses;
        this.Depositories = depositories;

        this.Types = new LocalizedTransactionTypes("Invoices");
    }

    public override string Caption => this["Invoices", Array.Empty<object>()];

    public Reference<Office> Offices { get; }

    public Reference<Partner> Partners { get; }

    public Reference<Warehouse> Warehouses { get; }

    public Reference<Depository> Depositories { get; }

    public LocalizedTransactionTypes Types { get; set; }

    protected override Task PreLoad()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(
                    this.Offices.Initialize(),
                    this.Warehouses.Initialize(),
                    this.Depositories.Initialize(),
                    this.Partners.Initialize());

                InvokeOnMainThread(() =>
                {
                    if (this._loginService?.Session != null && !this._loginService.Session.IsAdmin && this._loginService.Session.Accounts != null)
                    {
                        var allowedIds = this._loginService.Session.Accounts
                            .Where(a => a.Value.HasFlag(AccountAccessLevel.Operate) || a.Value.HasFlag(AccountAccessLevel.Read))
                            .Select(a => a.Key)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        this.Warehouses.Filter = w => !w.IsDisabled && allowedIds.Contains(w.Id);
                        this.Depositories.Filter = d => !d.IsDisabled && allowedIds.Contains(d.Id);
                        this.Offices.Filter = o => !o.IsDisabled && allowedIds.Contains(o.Id);
                    }
                    else
                    {
                        this.Warehouses.Filter = w => !w.IsDisabled;
                        this.Depositories.Filter = d => !d.IsDisabled;
                        this.Offices.Filter = o => !o.IsDisabled;
                    }

                    this.Warehouses.RaisePropertyChanged("List");
                    this.Depositories.RaisePropertyChanged("List");
                    this.Offices.RaisePropertyChanged("List");
                });
            }
            catch
            {
            }
        });

        return base.PreLoad();
    }

    protected override Task<int> CountFilteredListAsync(ListFilter filter)
    {
        return this._repository.CountInfoAsync(DateTime.MinValue, DateTime.MaxValue);
    }

    protected override Task<int> CountFilteredListByDateAsync(DateTime from, DateTime till)
    {
        return this._repository.CountInfoAsync(from, till);
    }

    protected override Task<IEnumerable<InvoiceInfo>> GetFilteredListAsync(ListFilter filter)
    {
        return this._repository.GetInfoAsync(DateTime.MinValue, DateTime.MaxValue);
    }

    protected override Task<IEnumerable<InvoiceInfo>> GetFilteredListByDateAsync(
      DateTime from,
      DateTime till)
    {
        return this._repository.GetInfoAsync(from, till);
    }

    public bool HasCreateAccess => this._authorizer.CanCreate();

    public ICommand CreateNewCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.OnCreateNewAsync), (Func<bool>)(() => !this.IsBusy && this.HasCreateAccess));
        }
    }

    protected virtual Task OnCreateNewAsync()
    {
        return this.NavigationService.Navigate<DetailsViewModel<Invoice>, string>(string.Empty);
    }

    public ICommand ViewDetailsCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.OnViewDetailsAsync), (Func<bool>)(() => !this.IsBusy && this.SelectedItem != null));
        }
    }

    protected virtual Task OnViewDetailsAsync()
    {
        return this.NavigationService.Navigate<DetailsViewModel<Invoice>, string>(this.SelectedItem.Id);
    }

    public ICommand SelectOrViewDetailsCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.OnSelectOrViewDetailsAsync), (Func<bool>)(() => !this.IsBusy && this.SelectedItem != null));
        }
    }

    protected virtual Task OnSelectOrViewDetailsAsync() => this.OnViewDetailsAsync();

    protected override Expression<Func<InvoiceInfo, bool>> GetDateFilter(DateTime from, DateTime till)
    {
        throw new NotImplementedException();
    }

    protected override Task<int> CountListAsync(
      params Expression<Func<InvoiceInfo, bool>>[] predicates)
    {
        throw new NotImplementedException();
    }

    protected override Task<IEnumerable<InvoiceInfo>> GetListAsync(
      params Expression<Func<InvoiceInfo, bool>>[] predicates)
    {
        throw new NotImplementedException();
    }
}