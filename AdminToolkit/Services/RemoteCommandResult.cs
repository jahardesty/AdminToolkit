using System.Collections.Generic;

namespace AdminToolkit.Services
{
    public sealed class RemoteCommandResult
    {
        public string Output { get; init; } = string.Empty;

        public string Error { get; init; } = string.Empty;

        public string Warning { get; init; } = string.Empty;

        public string Verbose { get; init; } = string.Empty;

        public string Debug { get; init; } = string.Empty;

        public IReadOnlyList<string> Information { get; init; } =
            new List<string>();

        public bool HadErrors { get; init; }
    }
}