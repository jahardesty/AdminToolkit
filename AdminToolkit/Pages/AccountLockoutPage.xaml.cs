using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.DirectoryServices.AccountManagement;
using System.DirectoryServices.ActiveDirectory;
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
    /// Interaction logic for AccountLockoutPage.xaml
    /// </summary>
    public partial class AccountLockoutPage : Page
    {
        public AccountLockoutPage()
        {
            InitializeComponent();
        }

        private void TxtLockoutUser_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                // This triggers the same logic as clicking the search button
                CheckLockout_Click(this, new RoutedEventArgs());
            }
        }
        private async void CheckLockout_Click(object sender, RoutedEventArgs e)
        {
            // 1. Capture UI Values
            string targetUser = txtLockoutUser.Text.Trim();
            if (!double.TryParse(txtDaysBack.Text, out double days)) { days = 1; }
            long millisecondsBack = (long)(days * 24 * 60 * 60 * 1000);

            // 2. Show the "Spinning" status
            btnCheckLockout.IsEnabled = false;
            statusArea.Visibility = Visibility.Visible; // Make the spinner appear
            var results = new List<LockoutEvent>();

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Domain domain = Domain.GetCurrentDomain();
                    string timeFilter = $"and System[TimeCreated[timediff(@SystemTime) <= {millisecondsBack}]]";
                    string userFilter = string.IsNullOrEmpty(targetUser) ? "" : $"and EventData[Data[@Name='TargetUserName']='{targetUser}']";
                    string query = $"*[System[(EventID=4740)] {timeFilter} {userFilter}]";

                    foreach (DomainController dc in domain.DomainControllers)
                    {
                        // Update the text to show which DC we are hitting
                        Dispatcher.Invoke(() => lblStatus.Text = $"Searching {dc.Name}...");

                        try
                        {
                            EventLogQuery eventsQuery = new EventLogQuery("Security", PathType.LogName, query) { Session = new EventLogSession(dc.Name) };
                            using (EventLogReader reader = new EventLogReader(eventsQuery))
                            {
                                for (EventRecord eventInstance = reader.ReadEvent(); eventInstance != null; eventInstance = reader.ReadEvent())
                                {
                                    results.Add(new LockoutEvent
                                    {
                                        Time = eventInstance.TimeCreated?.ToString() ?? "N/A",
                                        UserName = eventInstance.Properties[0].Value.ToString(),
                                        Source = eventInstance.Properties[1].Value.ToString(),
                                        DC = dc.Name
                                    });
                                }
                            }
                        }
                        catch { /* Skip offline DCs */ }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => MessageBox.Show($"Error: {ex.Message}"));
                }
            });

            // 3. Hide the status area when done
            dgResults.ItemsSource = results.OrderByDescending(r => r.Time);
            btnCheckLockout.IsEnabled = true;
            statusArea.Visibility = Visibility.Collapsed; // Hide the spinner
        }
        private void UnlockUser_Click(object sender, RoutedEventArgs e)
        {
            // 1. Get the selected row data
            var selectedEvent = dgResults.SelectedItem as LockoutEvent;
            if (selectedEvent == null) return;

            try
            {
                // 2. Connect to the Domain
                using (PrincipalContext pc = new PrincipalContext(ContextType.Domain))
                {
                    // 3. Find the user
                    UserPrincipal user = UserPrincipal.FindByIdentity(pc, selectedEvent.UserName);

                    if (user != null)
                    {
                        if (user.IsAccountLockedOut())
                        {
                            user.UnlockAccount();
                            MessageBox.Show($"Successfully unlocked {selectedEvent.UserName}!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show($"{selectedEvent.UserName} is not currently locked out.", "Notice");
                        }
                    }
                    else
                    {
                        MessageBox.Show("Could not find user in Active Directory.", "Error");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to unlock account: {ex.Message}", "Permission Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
