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
                    this.Warehouses.Filter = w => !w.IsDisabled && AccountSecurityHelper.CanRead(this._loginService, w.Id);
                    this.Depositories.Filter = d => !d.IsDisabled && AccountSecurityHelper.CanRead(this._loginService, d.Id);
                    this.Offices.Filter = o => !o.IsDisabled && AccountSecurityHelper.CanRead(this._loginService, o.Id);

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

    private IEnumerable<InvoiceInfo> ApplySecurityFilter(IEnumerable<InvoiceInfo> items)
    {
        if (items == null) return Enumerable.Empty<InvoiceInfo>();
        if (this._loginService?.Session == null || this._loginService.Session.IsAdmin || this._loginService.Session.Accounts == null)
            return items;

        return items.Where(x => AccountSecurityHelper.CanReadTransaction(this._loginService, x)).ToList();
    }

    protected override async Task<int> CountFilteredListAsync(ListFilter filter)
    {
        if (this._loginService?.Session == null || this._loginService.Session.IsAdmin || this._loginService.Session.Accounts == null)
            return await this._repository.CountInfoAsync(DateTime.MinValue, DateTime.MaxValue);

        var items = await this.GetFilteredListAsync(filter);
        return items?.Count() ?? 0;
    }

    protected override async Task<int> CountFilteredListByDateAsync(DateTime from, DateTime till)
    {
        if (this._loginService?.Session == null || this._loginService.Session.IsAdmin || this._loginService.Session.Accounts == null)
            return await this._repository.CountInfoAsync(from, till);

        var items = await this.GetFilteredListByDateAsync(from, till);
        return items?.Count() ?? 0;
    }

    protected override async Task<IEnumerable<InvoiceInfo>> GetFilteredListAsync(ListFilter filter)
    {
        var items = await this._repository.GetInfoAsync(DateTime.MinValue, DateTime.MaxValue);
        return this.ApplySecurityFilter(items);
    }

    protected override async Task<IEnumerable<InvoiceInfo>> GetFilteredListByDateAsync(DateTime from, DateTime till)
    {
        var items = await this._repository.GetInfoAsync(from, till);
        return this.ApplySecurityFilter(items);
    }

    public bool HasCreateAccess => this._authorizer.CanCreate();

    public ICommand CreateNewCommand => new MvxAsyncCommand(this.OnCreateNewAsync, () => !this.IsBusy && this.HasCreateAccess);

    protected virtual Task OnCreateNewAsync()
    {
        return this.NavigationService.Navigate<DetailsViewModel<Invoice>, string>(string.Empty);
    }

    public ICommand ViewDetailsCommand => new MvxAsyncCommand(this.OnViewDetailsAsync, () => !this.IsBusy && this.SelectedItem != null);

    protected virtual Task OnViewDetailsAsync()
    {
        return this.NavigationService.Navigate<DetailsViewModel<Invoice>, string>(this.SelectedItem.Id);
    }

    public ICommand SelectOrViewDetailsCommand => new MvxAsyncCommand(this.OnSelectOrViewDetailsAsync, () => !this.IsBusy && this.SelectedItem != null);

    protected virtual Task OnSelectOrViewDetailsAsync() => this.OnViewDetailsAsync();

    protected override Expression<Func<InvoiceInfo, bool>> GetDateFilter(DateTime from, DateTime till) => throw new NotImplementedException();
    protected override Task<int> CountListAsync(params Expression<Func<InvoiceInfo, bool>>[] predicates) => throw new NotImplementedException();
    protected override Task<IEnumerable<InvoiceInfo>> GetListAsync(params Expression<Func<InvoiceInfo, bool>>[] predicates) => throw new NotImplementedException();
}