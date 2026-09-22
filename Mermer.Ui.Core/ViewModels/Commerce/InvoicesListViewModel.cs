// Decompiled with JetBrains decompiler
// Type: Mermer.Ui.Core.ViewModels.Commerce.InvoicesListViewModel
// Assembly: Mermer.Ui.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DC92D011-8413-44AC-9F10-F866D891CF66
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Ui.Core.dll

using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Plugins.Messenger;
using Mermer.Commerce.Models;
using Mermer.Commerce.Services;
using Mermer.CRM.Models;
using Mermer.Enterprise.Models;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Data.Authorizers;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Commerce;

public class InvoicesListViewModel : ListViewModelBaseWithFilterDate<InvoiceInfo>
{
  private readonly IInvoicesRepository _repository;
  private readonly IListAuthorizer<Invoice> _authorizer;

  public InvoicesListViewModel(
    IMvxMessenger messenger,
    Reference<Office> offices,
    Reference<Partner> partners,
    Reference<Warehouse> warehouses,
    Reference<Depository> depositories,
    IInvoicesRepository repository,
    IListAuthorizer<Invoice> authorizer,
    IMvxNavigationService navigationService,
    IUserInteractionService userInteractionService)
    : base(messenger, navigationService, userInteractionService)
  {
    this._repository = repository;
    this._authorizer = authorizer;
    this.Offices = offices;
    this.Partners = partners;
    this.Warehouses = warehouses;
    this.Depositories = depositories;
    this.Types = new LocalizedTransactionTypes("Repricing");
  }

  public override string Caption => this["Invoices", Array.Empty<object>()];

  public Reference<Office> Offices { get; }

  public Reference<Partner> Partners { get; }

  public Reference<Warehouse> Warehouses { get; }

  public Reference<Depository> Depositories { get; }

  public LocalizedTransactionTypes Types { get; set; }

    protected override Task PreLoad()
    {
        // Не блокируем UI: инициализация тяжелых справочников уходит в фон
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(
                    this.Offices.Initialize(),
                    this.Warehouses.Initialize(),
                    this.Depositories.Initialize(),
                    this.Partners.Initialize());
            }
            catch
            {
                // Игнорируем фоновые ошибки инициализации справочников
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
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnCreateNewAsync), (Func<bool>) (() => !this.IsBusy && this.HasCreateAccess));
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
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnViewDetailsAsync), (Func<bool>) (() => !this.IsBusy && this.SelectedItem != null));
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
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnSelectOrViewDetailsAsync), (Func<bool>) (() => !this.IsBusy && this.SelectedItem != null));
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
