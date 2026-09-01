using System.Windows;
using System.Windows.Controls;
using GibddExamSimulator.ViewModels;

namespace GibddExamSimulator.Views;

public partial class ReviewErrorsView : UserControl
{
    public ReviewErrorsView() => InitializeComponent();

    private void BackToResult_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.ShowResultPage();
    }
}
