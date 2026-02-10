using System;
using System.DirectoryServices.AccountManagement;
using System.Windows;
using System.Windows.Controls;

namespace AdminToolkit.Pages
{
    public partial class ADSearchPage : Page
    {
        public ADSearchPage()
        {
            InitializeComponent();
            txtSearchUser.Focus();
        }

        private void TxtSearchUser_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                SearchUser_Click(this, new RoutedEventArgs());
            }
        }
        private void SearchUser_Click(object sender, RoutedEventArgs e)
        {
            string username = txtSearchUser.Text.Trim();
            txtSearchUser.Focus();
            txtSearchUser.SelectAll();

            if (string.IsNullOrEmpty(username)) return;

            try
            {
                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    var user = UserPrincipal.FindByIdentity(context, username);

                    if (user != null)
                    {
                        txtResultDN.Text = user.DistinguishedName;
                        btnCopy.IsEnabled = true;
                    }
                    else
                    {
                        txtResultDN.Text = "User not found.";
                        btnCopy.IsEnabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"AD Error: {ex.Message}");
            }

        }

        private void CopyDN_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(txtResultDN.Text))
            {
                Clipboard.SetText(txtResultDN.Text);
                MessageBox.Show("DN copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        private void Help_Click(object sender, RoutedEventArgs e)
        {
            string title = "AD Search Tool Guide";
            string instructions = "This tool is simple. Input the username, click search, and it will output the Distinguished name for that user.\n\n" +

                                   "WHY?\n" +
                                   "• Add a user to a sender approved list in AD for distribuition groups, this gives you the DN easily.";

            var helpWin = new ReadmeWindow(title, instructions);
            helpWin.Owner = Window.GetWindow(this); // Centers it to the main app
            helpWin.ShowDialog();
        }
    }
}