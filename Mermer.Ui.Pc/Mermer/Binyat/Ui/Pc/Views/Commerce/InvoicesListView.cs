using MvvmCross.Wpf.Views;
using System;
using System.Windows;

namespace Mermer.Ui.Pc.Views.Commerce;

public partial class InvoicesListView : MvxWpfView, IDisposable
{
    public InvoicesListView()
    {
        InitializeComponent();
    }

    public void Dispose()
    {
        try
        {
            // 1. Мгновенно отвязываем 5000 строк от грида ДО удаления из вкладок
            if (this.GridControl != null)
            {
                this.GridControl.ItemsSource = null;
            }
        }
        catch { }
    }
}