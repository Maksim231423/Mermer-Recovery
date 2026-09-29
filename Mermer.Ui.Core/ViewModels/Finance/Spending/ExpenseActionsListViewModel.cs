using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Plugins.Messenger;
using Mermer.Common.Settings;
using Mermer.Enterprise.Models;
using Mermer.Finance.Spending.Models;
using Mermer.Finance.Spending.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Finance.Spending;

public class ExpenseActionsListViewModel : ListViewModelBaseWithFilterDate<ExpenseAction>
{
    private readonly IConfigurator _configurator;
    private readonly IExpenseActionsRepository _repository;
    private List<object> _selectedDepositoryIds;
    private string _expenseId;
    private bool _loaded;

    public ExpenseActionsListViewModel(
        IMvxMessenger messenger,
        IConfigurator configurator,
        Reference<Expense> expenses,
        Reference<Depository> depositories,
        IExpenseActionsRepository repository,
        IMvxNavigationService navigationService,
        IUserInteractionService userInteractionService)
        : base(messenger, navigationService, userInteractionService)
    {
        this._configurator = configurator;
        this._repository = repository;
        this.Expenses = expenses;
        this.Depositories = depositories;
        this.Types = new LocalizedTransactionTypes("Repricing");
    }

    public List<object> SelectedDepositoryIds
    {
        get => this._selectedDepositoryIds;
        set
        {
            if (this._selectedDepositoryIds != null && value != null && this._selectedDepositoryIds.SequenceEqual(value))
                return;

            if (!this.SetProperty<List<object>>(ref this._selectedDepositoryIds, value, nameof(SelectedDepositoryIds)))
                return;

            if (_loaded && !this.IsBusy)
            {
                this.Initialize();
            }
        }
    }

    public string[] DepositoryIds
    {
        get
        {
            var list = this.SelectedDepositoryIds;
            if (list == null || !list.Any()) return Array.Empty<string>();

            return list
                .SelectMany(x => (x?.ToString() ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x) && Guid.TryParse(x, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public virtual string ExpenseId
    {
        get => this._expenseId;
        set
        {
            if (!this.SetProperty<string>(ref this._expenseId, value, nameof(ExpenseId)))
                return;

            if (_loaded && !this.IsBusy)
            {
                this.Initialize();
            }
        }
    }

    public Reference<Expense> Expenses { get; }
    public Reference<Depository> Depositories { get; }
    public LocalizedTransactionTypes Types { get; }

    protected override async Task PreLoad()
    {
        if (!_loaded && (SelectedDepositoryIds == null || !SelectedDepositoryIds.Any()))
        {
            AppSettings configAsync = await _configurator.GetConfigAsync<AppSettings>();
            if (configAsync != null && !string.IsNullOrEmpty(configAsync.DefaultDepositoryId))
            {
                SelectedDepositoryIds = new List<object> { configAsync.DefaultDepositoryId };
            }
        }

        await Task.WhenAll(
            base.PreLoad(),
            Expenses.Initialize(),
            Depositories.Initialize()
        );

        _loaded = true;
    }

    protected override Task<int> CountFilteredListByDateAsync(DateTime from, DateTime till)
    {
        return this._repository.CountAsync(from, till, this.DepositoryIds, this.ExpenseId);
    }

    protected override Task<int> CountFilteredListAsync(ListFilter filter)
    {
        return this._repository.CountAsync(null, null, this.DepositoryIds, this.ExpenseId);
    }

    protected override Task<IEnumerable<ExpenseAction>> GetFilteredListByDateAsync(DateTime from, DateTime till)
    {
        return this._repository.GetAsync(from, till, this.DepositoryIds, this.ExpenseId);
    }

    protected override Task<IEnumerable<ExpenseAction>> GetFilteredListAsync(ListFilter filter)
    {
        return this._repository.GetAsync(null, null, this.DepositoryIds, this.ExpenseId);
    }

    protected override Expression<Func<ExpenseAction, bool>> GetDateFilter(DateTime from, DateTime till) => throw new NotImplementedException();
    protected override Task<int> CountListAsync(params Expression<Func<ExpenseAction, bool>>[] predicates) => throw new NotImplementedException();
    protected override Task<IEnumerable<ExpenseAction>> GetListAsync(params Expression<Func<ExpenseAction, bool>>[] predicates) => throw new NotImplementedException();

    public ICommand SelectOrViewDetailsCommand
    {
        get
        {
            return new MvxAsyncCommand(this.OnSelectOrViewDetailsAsync, () => !this.IsBusy && this.SelectedItem != null);
        }
    }

    private Task OnSelectOrViewDetailsAsync()
    {
        return this.NavigationService.Navigate<DetailsViewModel<ExpenseSlip>, string>(this.SelectedItem.TransactionId);
    }
}