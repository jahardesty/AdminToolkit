using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AdminToolkit.Pages
{
    /// <summary>
    /// Interaction logic for WelcomePage.xaml
    /// </summary>
    public partial class WelcomePage : Page
    {
        public WelcomePage()
        {
            InitializeComponent();
            bool isAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

            lblAdminStatus.Text = isAdmin ? "Running as Administrator (Elevated)" : "Running as Standard User (Limited)";
            lblAdminStatus.Foreground = isAdmin ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
        }

        // WelcomePage.xaml.cs

        private void OpenReadme_Click(object sender, RoutedEventArgs e)
        {
            string title = "Admin Toolkit Overview";
            string message = "Welcome to the Admin Toolkit.\n\n" +
                             "• Use the sidebar to navigate between tools.\n" +
                             "• Each page has its own '?' button for specific instructions.";

            ReadmeWindow readme = new ReadmeWindow(title, message);
            readme.Owner = Window.GetWindow(this);
            readme.ShowDialog();
        }
    }
}
