using DevExpress.Xpf.WindowsUI;
using System.Windows;
using System.Windows.Media;

namespace Mermer.Ui.Pc.Controls.MenuItems;

public partial class CommonClose : AppBarButton
{
    public CommonClose() => InitializeComponent();

    private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // 1. Находим View на экране
        var parentView = FindParentView(this);

        // 2. Если ViewModel умеет закрываться штатно:
        if (this.DataContext is Mermer.Mvvm.ViewModels.BaseViewModel viewModel)
        {
            if (viewModel.CloseCommand != null && viewModel.CloseCommand.CanExecute(null))
            {
                viewModel.CloseCommand.Execute(null);
                return;
            }
        }

        // 3. Если команда не сработала, закрываем вкладку напрямую через родительское View
        if (parentView != null)
        {
            // Отправляем хинт презентеру закрыть этот конкретный View
            if (parentView.DataContext is MvvmCross.Core.ViewModels.IMvxViewModel mvxVm)
            {
                MvvmCross.Platform.Mvx.Resolve<MvvmCross.Core.Navigation.IMvxNavigationService>()?.Close(mvxVm);
            }
        }
    }

    // Радар: поднимается по дереву элементов вверх, пока не найдет главную форму
    private FrameworkElement FindParentView(DependencyObject child)
    {
        if (child == null) return null;

        DependencyObject parent = VisualTreeHelper.GetParent(child) ?? LogicalTreeHelper.GetParent(child);
        if (parent == null) return null;

        // Останавливаемся, когда находим главную форму (они наследуются от MvxWpfView или имеют "View" в названии)
        if (parent is MvvmCross.Wpf.Views.MvxWpfView || parent.GetType().Name.EndsWith("View"))
        {
            return parent as FrameworkElement;
        }

        return FindParentView(parent);
    }
}