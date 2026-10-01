using DevExpress.Xpf.Grid;
using MvvmCross.Wpf.Views;
using Mermer.StockManagement.Models;
using Mermer.Ui.Core.ViewModels.StockManagement;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Mermer.Ui.Pc.Views.StockManagement;

public partial class StockBalancesByDateAndWarehousesListView : MvxWpfView
{
    public StockBalancesByDateAndWarehousesListView() => InitializeComponent();

    private void GridControl_CustomUnboundColumnData(object sender, GridColumnDataEventArgs e)
    {
        if (!(DataContext is StockBalancesByDateAndWarehousesListViewModel dataContext))
            return;

        if (dataContext.List == null || e.ListSourceRowIndex < 0 || e.ListSourceRowIndex >= dataContext.List.Count)
            return;

        StockBalanceByWarehouses balanceByWarehouses = dataContext.List[e.ListSourceRowIndex];
        if (balanceByWarehouses == null)
            return;

        if (!e.IsGetData)
            return;

        if (balanceByWarehouses.Balances != null && balanceByWarehouses.Balances.TryGetValue(e.Column.FieldName, out var val))
        {
            e.Value = val;
        }
        else
        {
            e.Value = 0M;
        }
    }

    private void DetectShortCut(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F3)
            return;

        FirstFocus.Focus();
        e.Handled = true;
    }
}