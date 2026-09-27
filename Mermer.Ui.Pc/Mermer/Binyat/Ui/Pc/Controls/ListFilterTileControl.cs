using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Controls;
using DevExpress.Xpf.Navigation;

namespace Mermer.Ui.Pc.Controls;

public partial class ListFilterTileControl : TileBar
{
    private bool _isInternalUpdate;

    public ListFilterTileControl()
    {
        InitializeComponent();

        // 1. Выбор плитки пользователем в UI -> передача во ViewModel
        SelectionChanged += (s, e) =>
        {
            if (_isInternalUpdate || DataContext == null) return;

            var dc = DataContext;
            var prop = dc.GetType().GetProperty("SelectedFilter");
            if (prop == null || !prop.CanWrite) return;

            var selectedItem = SelectedItem;
            object? model = selectedItem is TileBarItem tile
                ? (tile.DataContext ?? tile.Content)
                : selectedItem;

            try
            {
                _isInternalUpdate = true;
                prop.SetValue(dc, model);
            }
            catch { }
            finally
            {
                _isInternalUpdate = false;
            }
        };

        // 2. Обновление выделения плитки, когда ViewModel меняет SelectedFilter
        DataContextChanged += (s, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged oldNpc)
            {
                oldNpc.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (e.NewValue is INotifyPropertyChanged newNpc)
            {
                newNpc.PropertyChanged += OnViewModelPropertyChanged;
                SyncSelectionFromViewModel(newNpc);
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == "SelectedFilter" && !_isInternalUpdate && sender != null)
        {
            SyncSelectionFromViewModel(sender);
        }
    }

    private void SyncSelectionFromViewModel(object vm)
    {
        var prop = vm.GetType().GetProperty("SelectedFilter");
        if (prop == null || !prop.CanRead) return;

        try
        {
            var filterValue = prop.GetValue(vm);
            _isInternalUpdate = true;
            SelectedItem = filterValue;
        }
        catch { }
        finally
        {
            _isInternalUpdate = false;
        }
    }
}