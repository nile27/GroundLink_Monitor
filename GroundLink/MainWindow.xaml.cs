using System.Windows;
using GroundLink.ViewModels;

namespace GroundLink;

/// <summary>
/// 메인 창. 화면 코드는 여기 두지 않고, ViewModel을 DataContext로 연결만 한다.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        // XAML의 {Binding ...}이 이 ViewModel의 속성을 찾아가게 됨
        DataContext = viewModel;
    }
}
