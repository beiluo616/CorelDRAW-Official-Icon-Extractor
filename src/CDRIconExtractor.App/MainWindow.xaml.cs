using System.Windows;
using CDRIconExtractor.App.ViewModels;

namespace CDRIconExtractor.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    private void IconTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: IconItemViewModel item })
            _viewModel.SelectedItem = item;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
