using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Platform;
using Mermer.Authorization.Services;
using Mermer.Common.Settings;
using Mermer.Enterprise.Models;
using Mermer.Reporting.Models;
using Mermer.Reporting.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Reporting;

public class RevenueReportViewModel : BaseViewModel
{
    private readonly IConfigurator _configurator;
    private readonly IRevenueReportsRepository _repository;
    private readonly ILoginService _loginService;
    private IEnumerable<RevenueReport> _list;
    private System.Collections.Generic.List<object> _selectedWarehouseIds;
    private DateTime _dateFilterFrom = DateTime.Today;
    private DateTime _dateFilterTill = DateTime.Today;
    private bool _loaded;
    public object SelectedItem { get; set; }
    public System.Windows.Input.ICommand SelectOrViewDetailsCommand { get; set; }

    public RevenueReportViewModel(
        IConfigurator configurator,
        Reference<Warehouse> warehouses,
        IRevenueReportsRepository repository,
        IMvxNavigationService navigationService,
        IUserInteractionService userInteractionService)
        : base(navigationService, userInteractionService)
    {
        this._configurator = configurator;
        this._repository = repository;
        this.Warehouses = warehouses;

        try { this._loginService = Mvx.Resolve<ILoginService>(); } catch { }
    }

    public Reference<Warehouse> Warehouses { get; }

    public virtual IEnumerable<RevenueReport> List
    {
        get => this._list;
        set => this.SetProperty<IEnumerable<RevenueReport>>(ref this._list, value, nameof(List));
    }

    public System.Collections.Generic.List<object> SelectedWarehouseIds
    {
        get => this._selectedWarehouseIds;
        set
        {
            if (!this.SetProperty<System.Collections.Generic.List<object>>(ref this._selectedWarehouseIds, value, nameof(SelectedWarehouseIds)))
                return;
            this.RaisePropertyChanged("WarehouseIds");
        }
    }

    public string[] WarehouseIds
    {
        get
        {
            var selectedWarehouseIds = this.SelectedWarehouseIds;
            var list = selectedWarehouseIds != null
                ? selectedWarehouseIds.Select(x => x?.ToString()).ToArray()
                : Array.Empty<string>();

            return list.Where(id => AccountSecurityHelper.CanRead(this._loginService, id)).ToArray();
        }
    }

    public DateTime DateFilterFrom
    {
        get => this._dateFilterFrom;
        set => this.SetProperty<DateTime>(ref this._dateFilterFrom, value, nameof(DateFilterFrom));
    }

    public DateTime DateFilterTill
    {
        get => this._dateFilterTill;
        set => this.SetProperty<DateTime>(ref this._dateFilterTill, value, nameof(DateFilterTill));
    }

    public DateTime DateFilterTillInclusive
    {
        get
        {
            DateTime dateTime = this.DateFilterTill;
            dateTime = dateTime.AddDays(1.0);
            return dateTime.Date;
        }
    }

    protected override async Task PreLoad()
    {
        if (this.Warehouses != null)
        {
            this.Warehouses.Filter = w => !w.IsDisabled && AccountSecurityHelper.CanRead(this._loginService, w.Id);
        }

        await Task.WhenAll(
            base.PreLoad(),
            Warehouses.Initialize()
        );

        if (!_loaded && !WarehouseIds.Any())
        {
            AppSettings configAsync = await _configurator.GetConfigAsync<AppSettings>();
            string defWh = configAsync?.DefaultWarehouseId;
            if (!string.IsNullOrEmpty(defWh) && AccountSecurityHelper.CanRead(this._loginService, defWh))
            {
                SelectedWarehouseIds = new List<object> { defWh };
            }
            else
            {
                var firstAllowed = this.Warehouses?.List?.FirstOrDefault();
                SelectedWarehouseIds = firstAllowed != null ? new List<object> { firstAllowed.Id } : new List<object>();
            }
        }

        _loaded = true;
    }

    protected override async Task OnLoad()
    {
        var safeWarehouseIds = this.WarehouseIds;
        if (safeWarehouseIds.Length == 0)
        {
            safeWarehouseIds = this.Warehouses?.List?.Select(w => w.Id).ToArray() ?? Array.Empty<string>();
        }

        IEnumerable<RevenueReport> async = await this._repository.GetAsync(safeWarehouseIds, this.DateFilterFrom, this.DateFilterTillInclusive);
        this.List = async?.Where(x => AccountSecurityHelper.CanRead(this._loginService, x.WarehouseId));
        this.SubCaption = $"{this.DateFilterFrom:MMM d} - {this.DateFilterTill:MMM d}";
    }

    public ICommand ReloadCommand
    {
        get
        {
            return new MvxAsyncCommand(this.OnReloadAsync, () => !this.IsBusy && this.WarehouseIds.Length != 0);
        }
    }

    public virtual Task OnReloadAsync() => this.Initialize();
}