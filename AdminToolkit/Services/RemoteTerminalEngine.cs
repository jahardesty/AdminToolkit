using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AdminToolkit.Services
{
    public enum RemoteShellMode
    {
        PowerShell,
        CommandPrompt
    }

    public sealed class RemoteTerminalEngine : IDisposable
    {
        private readonly SemaphoreSlim _executionLock = new(1, 1);

        private Runspace? _runspace;
        private PowerShell? _activePipeline;
        private bool _disposed;

        public bool IsConnected =>
            _runspace?.RunspaceStateInfo.State == RunspaceState.Opened;

        public string ComputerName { get; private set; } = string.Empty;

        public RemoteShellMode ShellMode { get; set; } =
            RemoteShellMode.PowerShell;

        public event EventHandler<string>? StatusChanged;

        public async Task ConnectAsync(
            string computerName,
            PSCredential? credential = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(computerName))
            {
                throw new ArgumentException(
                    "A computer name is required.",
                    nameof(computerName));
            }

            await DisconnectAsync();

            ComputerName = computerName.Trim();

            StatusChanged?.Invoke(
                this,
                $"Connecting to {ComputerName}...");

            await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Runspace newRunspace;

                    if (IsLocalComputer(ComputerName))
                    {
                        InitialSessionState sessionState =
                            InitialSessionState.CreateDefault();

                        newRunspace =
                            RunspaceFactory.CreateRunspace(sessionState);
                    }
                    else
                    {
                        WSManConnectionInfo connectionInfo =
                            new WSManConnectionInfo
                            {
                                ComputerName = ComputerName,
                                OpenTimeout = 15000,
                                OperationTimeout = 60000,
                                IdleTimeout = 30 * 60 * 1000
                            };

                        if (credential != null)
                        {
                            connectionInfo.Credential = credential;
                        }

                        newRunspace =
                            RunspaceFactory.CreateRunspace(connectionInfo);
                    }

                    newRunspace.Open();

                    _runspace = newRunspace;
                },
                cancellationToken);

            StatusChanged?.Invoke(
                this,
                $"Connected to {ComputerName}");
        }

        public async Task<RemoteCommandResult> ExecuteAsync(
            string command,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!IsConnected || _runspace == null)
            {
                throw new InvalidOperationException(
                    "No remote terminal session is connected.");
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                return new RemoteCommandResult();
            }

            await _executionLock.WaitAsync(cancellationToken);

            try
            {
                return await Task.Run(
                    () => ExecuteInternal(command, cancellationToken),
                    cancellationToken);
            }
            finally
            {
                _executionLock.Release();
            }
        }

        private RemoteCommandResult ExecuteInternal(
            string command,
            CancellationToken cancellationToken)
        {
            if (_runspace == null)
            {
                throw new InvalidOperationException(
                    "The runspace is unavailable.");
            }

            using PowerShell powerShell = PowerShell.Create();

            _activePipeline = powerShell;
            powerShell.Runspace = _runspace;

            if (ShellMode == RemoteShellMode.PowerShell)
            {
                powerShell.AddScript(command);
            }
            else
            {
                /*
                 * Execute CMD inside the same remote PowerShell runspace.
                 *
                 * CMD environment changes such as "set NAME=value" do not
                 * persist between separate cmd.exe calls. Current-directory
                 * changes are handled separately below.
                 */
                string cmdScript = BuildCmdScript(command);
                powerShell.AddScript(cmdScript);
            }

            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    () =>
                    {
                        try
                        {
                            powerShell.Stop();
                        }
                        catch
                        {
                            // The pipeline may already be complete.
                        }
                    });

            var results = powerShell.Invoke();

            StringBuilder output = new();
            StringBuilder errors = new();
            StringBuilder warnings = new();
            StringBuilder verbose = new();
            StringBuilder debug = new();
            List<string> information = new();

            foreach (PSObject item in results)
            {
                if (item != null)
                {
                    output.AppendLine(item.ToString());
                }
            }

            foreach (ErrorRecord error in powerShell.Streams.Error)
            {
                errors.AppendLine(error.ToString());
            }

            foreach (WarningRecord warning in powerShell.Streams.Warning)
            {
                warnings.AppendLine(warning.Message);
            }

            foreach (VerboseRecord record in powerShell.Streams.Verbose)
            {
                verbose.AppendLine(record.Message);
            }

            foreach (DebugRecord record in powerShell.Streams.Debug)
            {
                debug.AppendLine(record.Message);
            }

            foreach (InformationRecord record in
                     powerShell.Streams.Information)
            {
                information.Add(record.MessageData?.ToString() ?? string.Empty);
            }

            _activePipeline = null;

            return new RemoteCommandResult
            {
                Output = output.ToString(),
                Error = errors.ToString(),
                Warning = warnings.ToString(),
                Verbose = verbose.ToString(),
                Debug = debug.ToString(),
                Information = information,
                HadErrors = powerShell.HadErrors
            };
        }

        private static string BuildCmdScript(string command)
        {
            string escapedCommand = command.Replace("'", "''");

            /*
             * Special-case CD so the PowerShell runspace's current directory
             * remains synchronized with what the user entered in CMD mode.
             */
            return $$"""
                $commandText = '{{escapedCommand}}'

                if ($commandText -match '^\s*(cd|chdir)\s*(.*)$') {
                    $requestedPath = $Matches[2].Trim().Trim('"')

                    if ([string]::IsNullOrWhiteSpace($requestedPath)) {
                        (Get-Location).Path
                    }
                    else {
                        Set-Location -LiteralPath $requestedPath
                        (Get-Location).Path
                    }
                }
                else {
                    $currentPath = (Get-Location).Path
                    cmd.exe /d /q /c "cd /d `"$currentPath`" && $commandText"
                }
                """;
        }

        public async Task<string> GetPromptAsync(
            CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                return "Disconnected> ";
            }

            RemoteCommandResult result =
                await ExecutePowerShellInternalAsync(
                    "(Get-Location).Path",
                    cancellationToken);

            string path = result.Output.Trim();

            return ShellMode == RemoteShellMode.PowerShell
                ? $"PS {ComputerName}:{path}> "
                : $"{ComputerName}:{path}> ";
        }

        private async Task<RemoteCommandResult>
            ExecutePowerShellInternalAsync(
                string script,
                CancellationToken cancellationToken)
        {
            RemoteShellMode originalMode = ShellMode;

            try
            {
                ShellMode = RemoteShellMode.PowerShell;
                return await ExecuteAsync(script, cancellationToken);
            }
            finally
            {
                ShellMode = originalMode;
            }
        }

        public void CancelCurrentCommand()
        {
            try
            {
                _activePipeline?.Stop();
            }
            catch
            {
                // The command may already have stopped.
            }
        }

        public async Task DisconnectAsync()
        {
            Runspace? runspace = _runspace;
            _runspace = null;

            if (runspace == null)
            {
                ComputerName = string.Empty;
                return;
            }

            await Task.Run(
                () =>
                {
                    try
                    {
                        if (runspace.RunspaceStateInfo.State ==
                            RunspaceState.Opened)
                        {
                            runspace.Close();
                        }
                    }
                    finally
                    {
                        runspace.Dispose();
                    }
                });

            string disconnectedComputer = ComputerName;
            ComputerName = string.Empty;

            StatusChanged?.Invoke(
                this,
                $"Disconnected from {disconnectedComputer}");
        }

        private static bool IsLocalComputer(string computerName)
        {
            return computerName.Equals(
                       "localhost",
                       StringComparison.OrdinalIgnoreCase) ||
                   computerName.Equals(
                       "127.0.0.1",
                       StringComparison.OrdinalIgnoreCase) ||
                   computerName.Equals(
                       ".",
                       StringComparison.OrdinalIgnoreCase) ||
                   computerName.Equals(
                       Environment.MachineName,
                       StringComparison.OrdinalIgnoreCase);
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                nameof(RemoteTerminalEngine));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _activePipeline?.Stop();
            }
            catch
            {
                // Ignore shutdown errors.
            }

            _activePipeline?.Dispose();
            _activePipeline = null;

            if (_runspace != null)
            {
                try
                {
                    _runspace.Close();
                }
                catch
                {
                    // Ignore shutdown errors.
                }

                _runspace.Dispose();
                _runspace = null;
            }

            _executionLock.Dispose();
        }
    }
}