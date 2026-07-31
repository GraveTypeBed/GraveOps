namespace GraveOps.App.Models;

public sealed record CommandResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
    public string Combined => string.IsNullOrWhiteSpace(StdErr) ? StdOut : $"{StdOut}\n{StdErr}".Trim();
}

public sealed record SshTestResult(bool Success, string Message, string Fingerprint);

public sealed record DockerContainerRow(string Name, string Image, string Status, string Ports);
