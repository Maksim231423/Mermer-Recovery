using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Grid;
using System.Windows;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace Mermer.Ui.Pc.Controls;

public partial class StyledGridControl : GridControl
{
    public static readonly DependencyProperty AutoWidthProperty = DependencyProperty.Register(nameof(AutoWidth), typeof(bool), typeof(StyledGridControl), new PropertyMetadata((object)true));
    public static readonly DependencyProperty ShowGroupedColumnsProperty = DependencyProperty.Register(nameof(ShowGroupedColumns), typeof(bool), typeof(StyledGridControl), new PropertyMetadata((object)true));
    public static readonly DependencyProperty ShowTotalSummaryProperty = DependencyProperty.Register(nameof(ShowTotalSummary), typeof(bool), typeof(StyledGridControl), new PropertyMetadata((object)true));
    public static readonly DependencyProperty SearchControlProperty = DependencyProperty.Register(nameof(SearchControl), typeof(SearchControl), typeof(StyledGridControl), new PropertyMetadata((object)null));
    public static readonly DependencyProperty PrintButtonProperty = DependencyProperty.Register(nameof(PrintButton), typeof(ButtonBase), typeof(StyledGridControl), new PropertyMetadata((object)null));
    public static readonly DependencyProperty FilterButtonProperty = DependencyProperty.Register(nameof(FilterButton), typeof(ButtonBase), typeof(StyledGridControl), new PropertyMetadata((object)null));

    public StyledGridControl() => InitializeComponent();

    public bool AutoWidth
    {
        get => (bool)GetValue(AutoWidthProperty);
        set => SetValue(AutoWidthProperty, value);
    }

    public bool ShowGroupedColumns
    {
        get => (bool)GetValue(ShowGroupedColumnsProperty);
        set => SetValue(ShowGroupedColumnsProperty, value);
    }

    public bool ShowTotalSummary
    {
        get => (bool)GetValue(ShowTotalSummaryProperty);
        set => SetValue(ShowTotalSummaryProperty, value);
    }

    public SearchControl SearchControl
    {
        get => (SearchControl)GetValue(SearchControlProperty);
        set => SetValue(SearchControlProperty, value);
    }

    public ButtonBase PrintButton
    {
        get => (ButtonBase)GetValue(PrintButtonProperty);
        set => SetValue(PrintButtonProperty, value);
    }

    public ButtonBase FilterButton
    {
        get => (ButtonBase)GetValue(FilterButtonProperty);
        set => SetValue(FilterButtonProperty, value);
    }
}