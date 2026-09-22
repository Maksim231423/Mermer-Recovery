// Decompiled with JetBrains decompiler
// Type: Mermer.Ui.Core.Helpers.StockSearcher
// Assembly: Mermer.Ui.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DC92D011-8413-44AC-9F10-F866D891CF66
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Ui.Core.dll

using MvvmCross.Core.ViewModels;
using Mermer.StockManagement.Services;
using Mermer.Ui.Core.Services;
using Mermer.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.Helpers;

public class StockSearcher : BindableObject
{
  private readonly IStockSearchService _stockSearchService;
  private CancellationTokenSource _cancellationTokenSource;
  private string _searchText;
  private string _warehouseId;
  private string _priceGroup;
  private string _currencyId;
  private bool _showLastPurchasePrice;
  private bool _hideZeroBalance;
  private bool _hideDisabled = true;
  private IEnumerable<StockSearchResult> _searchResult;
  private bool _hasResult;
  private bool _hasLastPurchasePrice;
  private bool _hasBalance;
  private bool _willSearch;
  private bool _isSearching;

  public StockSearcher(IStockSearchService stockSearchService)
  {
    this._stockSearchService = stockSearchService;
  }

  public virtual string SearchText
  {
    get => this._searchText;
    set
    {
      if (!this.SetProperty<string>(ref this._searchText, value, nameof (SearchText)))
        return;
      this.SearchAsync(value);
    }
  }

  public string WarehouseId
  {
    get => this._warehouseId;
    set => this.SetProperty<string>(ref this._warehouseId, value, nameof (WarehouseId));
  }

  public virtual string PriceGroup
  {
    get => this._priceGroup;
    set => this.SetProperty<string>(ref this._priceGroup, value, nameof (PriceGroup));
  }

  public virtual string CurrencyId
  {
    get => this._currencyId;
    set => this.SetProperty<string>(ref this._currencyId, value, nameof (CurrencyId));
  }

  public bool ShowLastPurchasePrice
  {
    get => this._showLastPurchasePrice;
    set
    {
      this.SetProperty<bool>(ref this._showLastPurchasePrice, value, nameof (ShowLastPurchasePrice));
    }
  }

  public bool HideZeroBalance
  {
    get => this._hideZeroBalance;
    set
    {
      if (!this.SetProperty<bool>(ref this._hideZeroBalance, value, nameof (HideZeroBalance)))
        return;
      this.RaisePropertyChanged<IEnumerable<StockSearchResult>>((Expression<Func<IEnumerable<StockSearchResult>>>) (() => this.SearchResult));
    }
  }

  public virtual bool HideDisabled
  {
    get => this._hideDisabled;
    set
    {
      if (!this.SetProperty<bool>(ref this._hideDisabled, value, nameof (HideDisabled)))
        return;
      this.RaisePropertyChanged<IEnumerable<StockSearchResult>>((Expression<Func<IEnumerable<StockSearchResult>>>) (() => this.SearchResult));
    }
  }

  public virtual IEnumerable<StockSearchResult> SearchResult
  {
    get
    {
      IEnumerable<StockSearchResult> source = this._searchResult;
      if (source != null)
      {
        if (this.HideZeroBalance)
          source = source.Where<StockSearchResult>((Func<StockSearchResult, bool>) (x => x.Balance != 0M));
        if (this.HideDisabled)
          source = source.Where<StockSearchResult>((Func<StockSearchResult, bool>) (x => !x.IsDisabled));
      }
      return source;
    }
    set
    {
      this.SetProperty<IEnumerable<StockSearchResult>>(ref this._searchResult, value, nameof (SearchResult));
      IEnumerable<StockSearchResult> searchResult1 = this._searchResult;
      this.HasResult = searchResult1 != null && searchResult1.Any<StockSearchResult>();
      IEnumerable<StockSearchResult> searchResult2 = this._searchResult;
      this.HasBalance = searchResult2 != null && searchResult2.Any<StockSearchResult>((Func<StockSearchResult, bool>) (x => x.Balance != 0M));
      int num;
      if (this.ShowLastPurchasePrice)
      {
        IEnumerable<StockSearchResult> searchResult3 = this._searchResult;
        num = searchResult3 != null ? (searchResult3.Any<StockSearchResult>((Func<StockSearchResult, bool>) (x => x.LastPurchasePrice.HasValue)) ? 1 : 0) : 0;
      }
      else
        num = 0;
      this.HasLastPurchasePrice = num != 0;
    }
  }

  public virtual bool HasResult
  {
    get => this._hasResult;
    set => this.SetProperty<bool>(ref this._hasResult, value, nameof (HasResult));
  }

  public bool HasLastPurchasePrice
  {
    get => this._hasLastPurchasePrice;
    set
    {
      this.SetProperty<bool>(ref this._hasLastPurchasePrice, value, nameof (HasLastPurchasePrice));
    }
  }

  public bool HasBalance
  {
    get => this._hasBalance;
    set => this.SetProperty<bool>(ref this._hasBalance, value, nameof (HasBalance));
  }

  public bool WillSearch
  {
    get => this._willSearch;
    set => this.SetProperty<bool>(ref this._willSearch, value, nameof (WillSearch));
  }

  public bool IsSearching
  {
    get => this._isSearching;
    set => this.SetProperty<bool>(ref this._isSearching, value, nameof (IsSearching));
  }

    private StockSearchResult _selectedItem;

    public virtual StockSearchResult SelectedItem
    {
        get => this._selectedItem;
        set
        {
            if (this.SetProperty<StockSearchResult>(ref this._selectedItem, value, nameof(SelectedItem)))
            {
                if (value != null)
                {
                    // 1. Как только мы нажали Enter, передаем товар в главную програму!
                    this.Select(value);

                    // 2. Очищаем выбор, чтобы можно было искать следующий товар
                    this._selectedItem = null;
                    this.RaisePropertyChanged(() => this.SelectedItem);
                }
            }
        }
    }

    public Task Initialize(bool forceReload = false)
  {
    return this._stockSearchService is InMemoryStockSearchService stockSearchService ? stockSearchService.Initialize(forceReload) : Task.CompletedTask;
  }

  public ICommand ReinitializeCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnReinitializeAsync), (Func<bool>) (() => true));
    }
  }

  public virtual Task OnReinitializeAsync() => this.Initialize(true);

    public async Task SearchAsync(string text)
    {
        try
        {
            this._cancellationTokenSource?.Cancel();
            this.WillSearch = this.IsSearching = false;
            this._cancellationTokenSource = new CancellationTokenSource();

            if (string.IsNullOrWhiteSpace(text))
            {
                this.SearchResult = Enumerable.Empty<StockSearchResult>();
                return;
            }

            this.WillSearch = true;
            // ОПТИМИЗАЦИЯ: Уменьшаем задержку с 500 мс до 150 мс (мгновенный отклик)
            await Task.Delay(150, this._cancellationTokenSource.Token);
            this.WillSearch = false;
            this.IsSearching = true;

            this.SearchResult = await this._stockSearchService.Search(
                text.Trim(),
                this.WarehouseId,
                this.PriceGroup,
                this.CurrencyId,
                this._cancellationTokenSource.Token);

            this.IsSearching = false;
            this._cancellationTokenSource = null;
        }
        catch (OperationCanceledException)
        {
            // Игнорируем отмену предыдущего ввода при быстром наборе
        }
        catch (Exception)
        {
            this.IsSearching = false;
            this.WillSearch = false;
        }
    }

    public void Select(StockSearchResult result) => this.OnResultSelected(this, result);

  public event SearchResultSelected ResultSelected;

  protected virtual void OnResultSelected(StockSearcher searcher, StockSearchResult result)
  {
    SearchResultSelected resultSelected = this.ResultSelected;
    if (resultSelected == null)
      return;
    resultSelected(searcher, result);
  }
}
