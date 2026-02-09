using System.Windows;

namespace AdminToolkit.Pages // Ensure this matches your project's namespace
{
    public partial class ReadmeWindow : Window
    {
        // New constructor that accepts Title and Message
        public ReadmeWindow(string title, string message)
        {
            InitializeComponent();

            // Fill the labels with the passed-in strings
            TitleLabel.Text = title;
            ContentLabel.Text = message;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
