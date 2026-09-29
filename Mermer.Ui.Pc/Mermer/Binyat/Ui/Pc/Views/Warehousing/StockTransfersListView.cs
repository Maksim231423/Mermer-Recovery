using DevExpress.Xpf.Grid;
using MvvmCross.Wpf.Views;
using System.Linq;
using System.Windows;

namespace Mermer.Ui.Pc.Views.Warehousing;

public partial class StockTransfersListView : MvxWpfView
{
    public StockTransfersListView()
    {
        InitializeComponent();

        // Подписываемся на загрузку окна, чтобы GridControl уже успел создаться
        this.Loaded += StockTransfersListView_Loaded;
    }

    private void StockTransfersListView_Loaded(object sender, RoutedEventArgs e)
    {
        if (GridControl != null && GridControl.View is TableView tableView)
        {
            var existingCondition = tableView.FormatConditions
                .FirstOrDefault(x => x.Expression == "[ActionReceivedTotal] < [ActionTotal] And [IsDisabled] = False");

            if (existingCondition == null)
            {
                tableView.FormatConditions.Add(new FormatCondition
                {
                    Expression = "[ActionReceivedTotal] < [ActionTotal] And [IsDisabled] = False",
                    ApplyToRow = true,
                    PredefinedFormatName = "LightRedFillWithDarkRedText"
                });
            }
        }
    }
}