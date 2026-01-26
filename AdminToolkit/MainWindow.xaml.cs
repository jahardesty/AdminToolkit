using System.Windows;

using System.Windows.Documents;

namespace AdminToolkit
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            MainFrame.Navigate(new Pages.WelcomePage());
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

        private void NavWelcomePage_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new Pages.WelcomePage());
        }

        protected override void OnClosed(System.EventArgs e)
        {
            Application.Current.Shutdown();
            base.OnClosed(e);
        }
    }
}