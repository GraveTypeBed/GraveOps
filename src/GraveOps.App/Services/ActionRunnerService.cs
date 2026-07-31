using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class ActionRunnerService
{
    private readonly AppServices _services;
    public ActionRunnerService(AppServices services) => _services = services;

    public async Task<ActionRunResult> RunAsync(
        QuickAction action,
        ServerProfile server,
        CancellationToken token = default)
    {
        if (_services.Config.Current.Settings.SafeMode &&
            action.Risk != ActionRisk.ReadOnly)
        {
            const string message =
                "Safe Mode is enabled. This action is read-only only.";

            _services.Activity.Record(
                "Safe Mode blocked action",
                $"{action.Name}: {message}",
                ActivityLevel.Warning,
                serverId: server.Id,
                deepLink: $"action:{action.Name}");

            return new ActionRunResult(
                false,
                -3,
                "",
                message,
                message,
                TimeSpan.Zero);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var job = _services.Jobs.Begin(
            action.Name,
            server.Id,
            $"action:{action.Name}");

        using var linked =
            CancellationTokenSource.CreateLinkedTokenSource(token);

        _services.Jobs.RegisterCancellation(job, linked);

        try
        {
            var timeout =
                action.Name.Contains("Backup", StringComparison.OrdinalIgnoreCase) ||
                action.Name.Contains("Restore", StringComparison.OrdinalIgnoreCase)
                    ? 1800
                    : 180;

            _services.Jobs.Update(
                job,
                GraveJobState.Running,
                $"Running on {server.Name}");

            var command =
                await ExecuteActionCommandAsync(
                    server,
                    action.Command,
                    timeout,
                    linked.Token);

            var verification = "";
            var verified = command.ExitCode == 0;

            if (verified)
            {
                _services.Jobs.Update(
                    job,
                    GraveJobState.Running,
                    "Command completed; verifying intended state...");

                (verified, verification) =
                    await VerifyAsync(
                        action,
                        server,
                        linked.Token);
            }

            watch.Stop();

            var success =
                command.ExitCode == 0 && verified;

            var detail = success
                ? (string.IsNullOrWhiteSpace(verification)
                    ? "Completed successfully."
                    : verification)
                : (string.IsNullOrWhiteSpace(command.StdErr)
                    ? verification
                    : command.StdErr);

            _services.Jobs.Update(
                job,
                success
                    ? GraveJobState.Success
                    : GraveJobState.Failed,
                detail,
                success ? 100 : null);

            _services.Activity.Record(
                action.Name,
                detail,
                success
                    ? ActivityLevel.Success
                    : ActivityLevel.Error,
                watch.Elapsed.TotalSeconds,
                server.Id,
                $"action:{action.Name}");

            if (!success)
            {
                _services.Notifications.Record(
                    "GraveOps action failed",
                    $"{action.Name}: {detail}",
                    "ERROR",
                    $"action:{action.Name}");
            }

            return new ActionRunResult(
                success,
                command.ExitCode,
                command.StdOut,
                command.StdErr,
                verification,
                watch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            watch.Stop();

            _services.Jobs.Update(
                job,
                GraveJobState.Cancelled,
                "Cancelled");

            _services.Activity.Record(
                "Action cancelled",
                action.Name,
                ActivityLevel.Warning,
                watch.Elapsed.TotalSeconds,
                server.Id,
                $"action:{action.Name}");

            return new ActionRunResult(
                false,
                -2,
                "",
                "Cancelled",
                "",
                watch.Elapsed);
        }
        catch (Exception ex)
        {
            watch.Stop();

            _services.Jobs.Update(
                job,
                GraveJobState.Failed,
                ex.Message);

            _services.Activity.Record(
                action.Name,
                ex.Message,
                ActivityLevel.Error,
                watch.Elapsed.TotalSeconds,
                server.Id,
                $"action:{action.Name}");

            _services.Notifications.Record(
                "GraveOps action failed",
                $"{action.Name}: {ex.Message}",
                "ERROR",
                $"action:{action.Name}");

            return new ActionRunResult(
                false,
                -1,
                "",
                ex.Message,
                "",
                watch.Elapsed);
        }
        finally
        {
            _services.Jobs.ReleaseCancellation(job);
        }
    }


    private async Task<CommandResult> ExecuteActionCommandAsync(
        ServerProfile server,
        string command,
        int timeoutSeconds,
        CancellationToken token)
    {
        if (server.ConnectionKind == HostConnectionKind.RemoteLinux)
            return await _services.Ssh.ExecuteAsync(server, command, timeoutSeconds, token);

        if (server.ConnectionKind == HostConnectionKind.RemoteWindows)
        {
            try
            {
                var stdout = await _services.PowerShellRemote.ExecuteAsync(server, command, timeoutSeconds, token);
                return new CommandResult(0, stdout, "");
            }
            catch (Exception ex)
            {
                return new CommandResult(-1, "", ex.Message);
            }
        }

        if (server.ConnectionKind == HostConnectionKind.LocalWindows)
            return await _services.LocalPowerShell.ExecuteAsync(command, timeoutSeconds, token);

        return new CommandResult(-1, "", $"Actions are not implemented for {server.ConnectionKind}.");
    }

    private async Task<(bool Success, string Message)> VerifyAsync(
        QuickAction action,
        ServerProfile server,
        CancellationToken token)
    {
        if (server.ConnectionKind == HostConnectionKind.RemoteLinux &&
            action.Name.Equals(
                "Restart Plex",
                StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < 30; i++)
            {
                var check =
                    await _services.Ssh.ExecuteAsync(
                        server,
                        "systemctl is-active plexmediaserver 2>/dev/null; curl -fsS --max-time 2 http://127.0.0.1:32400/identity >/dev/null 2>&1 && echo PORT_OK || true",
                        15,
                        token);

                if (check.StdOut.Contains(
                        "active",
                        StringComparison.OrdinalIgnoreCase) &&
                    check.StdOut.Contains(
                        "PORT_OK",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var info =
                        await _services.Ssh.ExecuteAsync(
                            server,
                            "systemctl show plexmediaserver -p MainPID -p ActiveEnterTimestamp --no-pager",
                            15,
                            token);

                    return (
                        true,
                        "Plex returned active and port 32400 is reachable.\n" +
                        info.StdOut.Trim());
                }

                await Task.Delay(1000, token);
            }

            return (
                false,
                "Plex restart command exited successfully, but Plex did not become reachable within 30 seconds.");
        }

        return (true, "");
    }
}