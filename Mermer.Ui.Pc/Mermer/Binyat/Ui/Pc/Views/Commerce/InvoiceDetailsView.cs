using Mermer.Mvvm.ViewModels;
using Mermer.Ui.Core.ViewModels.Commerce;
using MvvmCross.Wpf.Views;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Mermer.Ui.Pc.Views.Commerce;

public partial class InvoiceDetailsView : MvxWpfView
{
    public InvoiceDetailsView() => InitializeComponent();

    private void DetectShortCut(object sender, KeyEventArgs e)
    {
        if (!(DataContext is InvoiceDetailsViewModel dataContext))
            return;

        switch (e.Key)
        {
            case Key.End:
                dataContext.UpdatePaymentCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Insert:
                dataContext.SelectedLinePlusOneCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete:
                dataContext.SelectedLineMinusOneCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F3:
                FirstFocus.Focus();
                e.Handled = true;
                break;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is InvoiceDetailsViewModel invoiceVm)
        {
            if (invoiceVm.CloseCommand != null && invoiceVm.CloseCommand.CanExecute(null))
            {
                invoiceVm.CloseCommand.Execute(null);
                return;
            }
        }

        if (DataContext is Mermer.Mvvm.ViewModels.BaseViewModel viewModel)
        {
            if (viewModel.CloseCommand != null && viewModel.CloseCommand.CanExecute(null))
            {
                viewModel.CloseCommand.Execute(null);
            }
        }
    }
}