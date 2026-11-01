using MvvmCross.Wpf.Views;
using System;
using System.Windows;

namespace Mermer.Ui.Pc.Views.Settings;

public partial class ActivationView : MvxWpfView
{
    public ActivationView() => InitializeComponent();

    private void ActivationView_OnLoaded(object sender, RoutedEventArgs e)
    {
        //ClientNote.Text = ServerNote.Text = $"{Environment.MachineName} - {Environment.UserName}";
    }
}