using AdminToolkit.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace AdminToolkit.Pages
{
    public partial class RemoteCLIPage : Page
    {
        private readonly RemoteTerminalEngine _terminalEngine = new();
        private readonly List<string> _commandHistory = new();

        private int _historyIndex;
        private CancellationTokenSource? _commandCancellation;

        public RemoteCLIPage()
        {
            InitializeComponent();

            _terminalEngine.StatusChanged += TerminalEngine_StatusChanged;

            Unloaded += RemoteCLIPage_Unloaded;

            AppendTerminalText(
                "Welcome to Remote CLI.\n",
                Colors.DarkGray);

            AppendTerminalText(
                "Enter a target computer and click Connect.\n\n",
                Colors.DarkGray);
        }

        private async void ConnectButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string target = TargetComputerBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(target))
            {
                MessageBox.Show(
                    "Enter a computer name.",
                    "Remote CLI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            SetConnectingState();

            try
            {
                await _terminalEngine.ConnectAsync(target);

                SetConnectedState();

                AppendTerminalText(
                    $"[System] Connected to {target}.\n",
                    Colors.Cyan);

                await DisplayPromptAsync();

                CommandInput.Focus();
            }
            catch (Exception ex)
            {
                SetDisconnectedState();

                AppendTerminalText(
                    $"[Connection Error] {ex.Message}\n",
                    Colors.Coral);
            }
        }

        private async void DisconnectButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await DisconnectAsync();
        }

        private async Task DisconnectAsync()
        {
            _commandCancellation?.Cancel();

            await _terminalEngine.DisconnectAsync();

            SetDisconnectedState();

            AppendTerminalText(
                "[System] Session disconnected.\n",
                Colors.DarkGray);
        }

        private async Task ExecuteCommandAsync()
        {
            string command = CommandInput.Text.Trim();

            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            if (!_terminalEngine.IsConnected)
            {
                AppendTerminalText(
                    "[Error] Connect to a computer first.\n",
                    Colors.Coral);

                return;
            }

            AddToHistory(command);

            CommandInput.Clear();
            SetCommandRunningState(true);

            AppendTerminalText(
                command + Environment.NewLine,
                Colors.Yellow);

            _commandCancellation = new CancellationTokenSource();

            try
            {
                RemoteCommandResult result =
                    await _terminalEngine.ExecuteAsync(
                        command,
                        _commandCancellation.Token);

                DisplayResult(result);
            }
            catch (OperationCanceledException)
            {
                AppendTerminalText(
                    "[System] Command cancelled.\n",
                    Colors.Orange);
            }
            catch (Exception ex)
            {
                AppendTerminalText(
                    $"[Execution Error] {ex.Message}\n",
                    Colors.Crimson);
            }
            finally
            {
                _commandCancellation.Dispose();
                _commandCancellation = null;

                SetCommandRunningState(false);

                if (_terminalEngine.IsConnected)
                {
                    await DisplayPromptAsync();
                    CommandInput.Focus();
                }
            }
        }

        private void DisplayResult(RemoteCommandResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.Output))
            {
                AppendTerminalText(
                    result.Output,
                    Colors.White);
            }

            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                AppendTerminalText(
                    result.Warning,
                    Colors.Orange);
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                AppendTerminalText(
                    result.Error,
                    Colors.Crimson);
            }

            if (!string.IsNullOrWhiteSpace(result.Verbose))
            {
                AppendTerminalText(
                    result.Verbose,
                    Colors.Gray);
            }

            if (!string.IsNullOrWhiteSpace(result.Debug))
            {
                AppendTerminalText(
                    result.Debug,
                    Colors.MediumPurple);
            }

            foreach (string information in result.Information)
            {
                AppendTerminalText(
                    information + Environment.NewLine,
                    Colors.LightBlue);
            }
        }

        private async Task DisplayPromptAsync()
        {
            string prompt =
                await _terminalEngine.GetPromptAsync();

            AppendTerminalText(
                prompt,
                Colors.LightGreen);
        }

        private async void CommandInput_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ExecuteCommandAsync();
                return;
            }

            if (e.Key == Key.Up)
            {
                e.Handled = true;
                ShowPreviousHistoryItem();
                return;
            }

            if (e.Key == Key.Down)
            {
                e.Handled = true;
                ShowNextHistoryItem();
            }
        }

        private async void SendButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await ExecuteCommandAsync();
        }

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _commandCancellation?.Cancel();
            _terminalEngine.CancelCurrentCommand();
        }

        private void PowerShellMode_Checked(
            object sender,
            RoutedEventArgs e)
        {
            _terminalEngine.ShellMode =
                RemoteShellMode.PowerShell;
        }

        private void CmdMode_Checked(
            object sender,
            RoutedEventArgs e)
        {
            _terminalEngine.ShellMode =
                RemoteShellMode.CommandPrompt;
        }

        private void AddToHistory(string command)
        {
            if (_commandHistory.Count == 0 ||
                !_commandHistory[^1].Equals(
                    command,
                    StringComparison.Ordinal))
            {
                _commandHistory.Add(command);
            }

            _historyIndex = _commandHistory.Count;
        }

        private void ShowPreviousHistoryItem()
        {
            if (_commandHistory.Count == 0)
            {
                return;
            }

            _historyIndex =
                Math.Max(0, _historyIndex - 1);

            CommandInput.Text =
                _commandHistory[_historyIndex];

            CommandInput.CaretIndex =
                CommandInput.Text.Length;
        }

        private void ShowNextHistoryItem()
        {
            if (_commandHistory.Count == 0)
            {
                return;
            }

            _historyIndex =
                Math.Min(
                    _commandHistory.Count,
                    _historyIndex + 1);

            CommandInput.Text =
                _historyIndex == _commandHistory.Count
                    ? string.Empty
                    : _commandHistory[_historyIndex];

            CommandInput.CaretIndex =
                CommandInput.Text.Length;
        }

        private void AppendTerminalText(
            string text,
            Color color)
        {
            TextPointer end =
                TerminalBox.Document.ContentEnd;

            TextRange range =
                new TextRange(end, end)
                {
                    Text = text
                };

            range.ApplyPropertyValue(
                TextElement.ForegroundProperty,
                new SolidColorBrush(color));

            TerminalBox.ScrollToEnd();
        }

        private void TerminalEngine_StatusChanged(
            object? sender,
            string status)
        {
            Dispatcher.Invoke(
                () => StatusText.Text = status);
        }

        private void SetConnectingState()
        {
            ConnectButton.IsEnabled = false;
            TargetComputerBox.IsEnabled = false;
            CommandInput.IsEnabled = false;
            SendButton.IsEnabled = false;

            StatusText.Text = "Connecting...";
            StatusText.Foreground = Brushes.Orange;
        }

        private void SetConnectedState()
        {
            ConnectButton.IsEnabled = false;
            DisconnectButton.IsEnabled = true;
            TargetComputerBox.IsEnabled = false;
            CommandInput.IsEnabled = true;
            SendButton.IsEnabled = true;

            StatusText.Text =
                $"Connected to {_terminalEngine.ComputerName}";

            StatusText.Foreground = Brushes.LightGreen;
        }

        private void SetDisconnectedState()
        {
            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
            TargetComputerBox.IsEnabled = true;
            CommandInput.IsEnabled = false;
            SendButton.IsEnabled = false;
            CancelButton.IsEnabled = false;

            StatusText.Text = "Disconnected";
            StatusText.Foreground = Brushes.Gray;
        }

        private void SetCommandRunningState(bool running)
        {
            CommandInput.IsEnabled = !running;
            SendButton.IsEnabled = !running;
            CancelButton.IsEnabled = running;
        }

        private async void RemoteCLIPage_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            _terminalEngine.StatusChanged -=
                TerminalEngine_StatusChanged;

            await _terminalEngine.DisconnectAsync();
            _terminalEngine.Dispose();
        }
    }
}