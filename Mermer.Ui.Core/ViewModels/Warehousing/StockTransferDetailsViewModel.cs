// Decompiled with JetBrains decompiler
// Type: Mermer.Ui.Core.ViewModels.Warehousing.StockTransferDetailsViewModel
// Assembly: Mermer.Ui.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DC92D011-8413-44AC-9F10-F866D891CF66
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Ui.Core.dll

using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Data;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.FundsManagement.Models;
using Mermer.FundsManagement.Models.Extenders;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.Services;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Ui.Core.ViewModels.Transactions;
using Mermer.Warehousing.Models;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;


#nullable disable
namespace Mermer.Ui.Core.ViewModels.Warehousing;

public class StockTransferDetailsViewModel : 
  StockTransactionDetailsViewModel<StockTransfer, StockTransferLine>,
  IMvxViewModel<StockTransferDetailsViewModel.Params>,
  IMvxViewModel
{
  private readonly IPrintingService _printingService;
  private string _sourceWarehouseId;
  private string _destinationWarehouseId;

  public StockTransferDetailsViewModel(
    CopyCreate copyCreate,
    IConfigurator configurator,
    ILoginService loginService,
    StockSearcher stockSearcher,
    Reference<Currency> currencies,
    Reference<Warehouse> warehouses,
    IPrintingService printingService,
    IStocksRepository stocksRepository,
    IRepository<StockTransfer> repository,
    IListAuthorizer<StockTransfer> authorizer,
    IMvxNavigationService navigationService,
    ITransactionCodeGenerationService codegentor,
    IUserInteractionService userInteractionService)
    : base(copyCreate, repository, authorizer, configurator, loginService, stockSearcher, currencies, warehouses, stocksRepository, navigationService, codegentor, userInteractionService)
  {
    this._printingService = printingService;
  }

  public void Prepare(StockTransferDetailsViewModel.Params parameter)
  {
    this._sourceWarehouseId = parameter.SourceWarehouseId;
    this._destinationWarehouseId = parameter.DestinationWarehouseId;
    this.Prepare(parameter.Lines);
  }

    protected override async Task OnLoad()
    {
        await base.OnLoad();

        if (!string.IsNullOrEmpty(ItemId))
            return;

        if (!string.IsNullOrEmpty(_sourceWarehouseId) && IsAllowedAccount(_sourceWarehouseId))
            Details.WarehouseId = _sourceWarehouseId;

        if (!string.IsNullOrEmpty(_destinationWarehouseId) && IsAllowedAccount(_destinationWarehouseId))
            Details.DestinationWarehouseId = _destinationWarehouseId;
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        // Если в новом документе оказался запрещенный склад, сбрасываем его
        if (string.IsNullOrEmpty(ItemId))
        {
            if (!IsAllowedAccount(Details.WarehouseId))
                Details.WarehouseId = Warehouses?.List?.FirstOrDefault(w => !w.IsDisabled && IsAllowedAccount(w.Id))?.Id;

            if (!IsAllowedAccount(Details.DestinationWarehouseId))
                Details.DestinationWarehouseId = null;
        }

        var usedWarehouseIds = new[] { Details.WarehouseId, Details.DestinationWarehouseId }.Distinct();

        // Скрываем все запрещенные склады из выпадающих списков
        Warehouses.Filter = x => (!x.IsDisabled || usedWarehouseIds.Contains(x.Id)) && IsAllowedAccount(x.Id);
        Warehouses?.RaisePropertyChanged("List");
    }

    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            // 1. Проверка выбора склада-отправителя
            if (string.IsNullOrEmpty(Details.WarehouseId))
            {
                throw new Exception(this["Field '{0}' is required", this["Source Warehouse"]]);
            }

            // 2. Проверка выбора склада-получателя
            if (string.IsNullOrEmpty(Details.DestinationWarehouseId))
            {
                throw new Exception(this["Field '{0}' is required", this["Destination Warehouse"]]);
            }

            // 3. Склад-отправитель и склад-получатель не должны совпадать
            if (Details.WarehouseId == Details.DestinationWarehouseId)
            {
                throw new Exception(this["Source and destination warehouses must be different"]);
            }

            // 5. Проверка каждой строки перемещения
            foreach (var line in Details.Lines)
            {
                // Количество должно быть строго больше нуля
                if (line.Quantity <= 0)
                    throw new Exception(this["Quantity must be greater than zero"]);

                // Проверка привязки валюты (если она есть в структуре StockTransferLine)
                if (string.IsNullOrEmpty(line.CurrencyId))
                    throw new Exception(this["Field '{0}' is required", this["Currency"]]);
            }
        }
        catch (Exception ex)
        {
            // Прерываем процесс сохранения и показываем окно с ошибкой
            UserInteractionService.ShowExceptionMessage(ex);
            return false;
        }

        // Если всё заполнено корректно — выполняем стандартное базовое сохранение
        return await base.OnSaveAsync();
    }


    protected override StockTransferLine CreateNewLine(
    Stock stock,
    Decimal? quantity = null,
    string unitId = null,
    Decimal? price = null,
    string currencyId = null)
  {
    StockTransferLine newLine = base.CreateNewLine(stock, quantity, unitId, price, currencyId);
    newLine.ReceivedId = Guid.NewGuid().ToString();
    newLine.ReceivedUnitId = newLine.UnitId;
    return newLine;
  }

    protected override async Task OnSelectedLineEditAsync()
    {
        StockTransferDetailsViewModel detailsViewModel = this;
        Stock stocksCacheAsync = await detailsViewModel.GetFromStocksCacheAsync(detailsViewModel.SelectedLine.StockId);
        IMvxNavigationService navigationService = detailsViewModel.NavigationService;
        StockTransferDetailsLineEditViewModel.Params @params = new StockTransferDetailsLineEditViewModel.Params();
        @params.StockCode = stocksCacheAsync.Code;
        @params.StockName = stocksCacheAsync.Name;
        @params.Quantity = detailsViewModel.SelectedLine.Quantity;
        @params.UnitId = detailsViewModel.SelectedLine.UnitId;
        @params.ReceivedQuantity = detailsViewModel.SelectedLine.ReceivedQuantity;
        @params.ReceivedUnitId = detailsViewModel.SelectedLine.ReceivedUnitId;
        @params.Units = (IEnumerable<StockUnit>)stocksCacheAsync.Units;
        CancellationToken cancellationToken = new CancellationToken();
        StockTransferDetailsLineEditViewModel.Result result = await navigationService.Navigate<StockTransferDetailsLineEditViewModel, StockTransferDetailsLineEditViewModel.Params, StockTransferDetailsLineEditViewModel.Result>(@params, cancellationToken: cancellationToken);
        if (result == null)
            return;
        detailsViewModel.SelectedLine.Quantity = result.Quantity;
        detailsViewModel.SelectedLine.UnitId = result.UnitId;
        detailsViewModel.SelectedLine.ReceivedQuantity = result.ReceivedQuantity;
        detailsViewModel.SelectedLine.ReceivedUnitId = result.ReceivedUnitId;
    }

    protected override bool AllowStockTracking()
  {
    return this.Details != null && this.Details.IsCompleted && !this.Details.IsConflicted;
  }

  public ICommand SelectDestinationWarehouseCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.SelectDestinationWarehouseAsync), (Func<bool>) (() => !this.IsBusy && this.HasSaveAccess));
    }
  }

    private bool IsAllowedAccount(string accountId)
    {
        if (string.IsNullOrEmpty(accountId) || LoginService?.Session == null || LoginService.Session.IsAdmin)
            return true;

        if (LoginService.Session.Accounts != null && LoginService.Session.Accounts.TryGetValue(accountId, out var level))
        {
            return level.HasFlag(AccountAccessLevel.Operate) || level.HasFlag(AccountAccessLevel.Read);
        }
        return true;
    }

    private async Task SelectDestinationWarehouseAsync()
  {
    StockTransferDetailsViewModel detailsViewModel = this;
    StockTransfer stockTransfer = detailsViewModel.Details;
    stockTransfer.DestinationWarehouseId = await detailsViewModel.NavigationService.Navigate<ListViewModel<Warehouse>, string, string>(detailsViewModel.Details.DestinationWarehouseId ?? Guid.Empty.ToString());
    stockTransfer = (StockTransfer) null;
  }

  public ICommand EqualizeSentAndRecieved
  {
    get
    {
      return (ICommand) new MvxCommand(new Action(this.OnEqualizeSentAndRecieved), (Func<bool>) (() =>
      {
        if (this.IsBusy || !this.HasSaveAccess)
          return false;
        StockTransfer details = this.Details;
        bool? nullable;
        if (details == null)
        {
          nullable = new bool?();
        }
        else
        {
          WatchedObservableCollection<StockTransferLine> lines = details.Lines;
          nullable = lines != null ? new bool?(lines.Any<StockTransferLine>()) : new bool?();
        }
        return nullable.GetValueOrDefault();
      }));
    }
  }

  private void OnEqualizeSentAndRecieved()
  {
    foreach (StockTransferLine line in (Collection<StockTransferLine>) this.Details.Lines)
    {
      line.ReceivedQuantity = line.Quantity;
      line.ReceivedUnitId = line.UnitId;
    }
  }

  public ICommand PrintCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnPrintCommandAsync), (Func<bool>) (() => !this.IsBusy && !this.IsDirty));
    }
  }

  protected virtual async Task OnPrintCommandAsync()
  {
    StockTransferDetailsViewModel detailsViewModel = this;
    await detailsViewModel._printingService.PrintStockTransfer(detailsViewModel.Details, true);
  }

    protected override void Details_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "DisplayCurrencyId")
        {
            var newCurrencyId = this.Details.DisplayCurrencyId;

            // Оновлюємо пошуковик
            if (StockSearcher != null && !string.IsNullOrEmpty(newCurrencyId))
            {
                var prop = StockSearcher.GetType().GetProperty("CurrencyId");
                if (prop != null)
                {
                    prop.SetValue(StockSearcher, newCurrencyId);
                }
            }

            // =========================================================
            // БЛОК ПРАВИЛЬНОЇ КОНВЕРТАЦІЇ ЦІН (USD <-> TMT)
            // =========================================================
            if (this.Details.Lines != null && !string.IsNullOrEmpty(newCurrencyId))
            {
                var targetCurrency = this.Currencies?.List?.FirstOrDefault(c => c.Id == newCurrencyId);

                foreach (var line in this.Details.Lines)
                {
                    // Пропускаємо, якщо рядок вже в потрібній валюті
                    if (string.IsNullOrEmpty(line.CurrencyId) || line.CurrencyId == newCurrencyId)
                        continue;

                    var sourceCurrency = this.Currencies?.List?.FirstOrDefault(c => c.Id == line.CurrencyId);

                    if (sourceCurrency != null && targetCurrency != null)
                    {
                        var sourceRate = sourceCurrency.GetRate(this.Details.Date);
                        var targetRate = targetCurrency.GetRate(this.Details.Date);

                        if (sourceRate != null && targetRate != null && sourceRate.Divider != 0 && targetRate.Multiplier != 0)
                        {
                            decimal sMult = sourceRate.Multiplier;
                            decimal sDiv = sourceRate.Divider;
                            decimal tMult = targetRate.Multiplier;
                            decimal tDiv = targetRate.Divider;

                            // Правильна математика: Ціна * (КурсСтароїВалюти) / (КурсНовоїВалюти)
                            line.Price = Math.Round(line.Price * (sMult / sDiv) * (tDiv / tMult), targetCurrency.Decimals);
                            line.CurrencyId = newCurrencyId;
                        }
                    }
                }
            }
            // =========================================================

            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(150);
                InvokeOnMainThread(() =>
                {
                    if (this.Details.Lines != null)
                    {
                        foreach (var line in this.Details.Lines)
                        {
                            line.RaisePropertyChanged("Price");
                            line.RaisePropertyChanged("DisplayTotal");
                            line.RaisePropertyChanged("ActionTotal");
                            line.RaisePropertyChanged("ActionReceivedTotal");
                        }
                    }
                    this.Details.RaisePropertyChanged("DisplayTotal");
                    this.Details.RaisePropertyChanged("ActionTotal");
                    this.Details.RaisePropertyChanged("ActionReceivedTotal");
                    this.Details.RaisePropertyChanged("Lines");
                });
            });

            // Блокуємо старий, зламаний код ядра
            return;
        }

        base.Details_PropertyChanged(sender, e);
    }

    public class Params
  {
    public string SourceWarehouseId { get; set; }

    public string DestinationWarehouseId { get; set; }

    public IEnumerable<CopyCreateLine> Lines { get; set; }
  }
}
