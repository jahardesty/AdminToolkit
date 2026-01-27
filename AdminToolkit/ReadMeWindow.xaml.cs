using System.Windows;

namespace AdminToolkit.Pages // Ensure this matches your project's namespace
{
    public partial class ReadmeWindow : Window
    {
        public ReadmeWindow()
        {
            InitializeComponent();
        }

        // This is the missing piece! 
        // It must be private or public, NOT static.
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
