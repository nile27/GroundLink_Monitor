using System.Windows;
using GroundLink.ViewModels;



namespace GroundLink
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public int CountRealizedCards()
        {
            int count = 0;
            for (int i = 0; i < SatelliteList.Items.Count; i++)
            {
                if (SatelliteList.ItemContainerGenerator.ContainerFromIndex(i) != null)
                    count++;
            }
            return count;
        }
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }


    }
}