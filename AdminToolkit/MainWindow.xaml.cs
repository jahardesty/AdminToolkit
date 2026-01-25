using System.Windows;

using System.Windows.Documents;

namespace AdminToolkit
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
        private void NavCopier_Click(object sender, RoutedEventArgs e)
        {
            //coming soon
            MainFrame.Navigate(new Pages.CopierPage());
        }

        private void NavPurger_Click(object sender, RoutedEventArgs e)
        {
            // coming soon
            MainFrame.Navigate(new Pages.PurgerPage());
        }

        private void NavArchive_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new Pages.ArchiveUserPage());
        }
    }
}