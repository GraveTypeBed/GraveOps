using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private string _activeDirectIntegrationName =
        string.Empty;

    private void ActivateDirectIntegration(
        string integrationName)
    {
        _activeDirectIntegrationName =
            integrationName;

        SelectIntegrationByName(
            integrationName);

        Get<TabControl>("DirectIntegrationTabs")
            .SelectedIndex = 0;

        PopulateDirectIntegrationWorkspace();
    }

    private OpsIntegration? ActiveDirectIntegration() =>
        _integrations.FirstOrDefault(item =>
            item.Name.Equals(
                _activeDirectIntegrationName,
                StringComparison.OrdinalIgnoreCase));

    private void PopulateDirectIntegrationWorkspace()
    {
        var integration =
            ActiveDirectIntegration();

        var descriptor =
            ModularApplicationDescriptor.For(
                _activeDirectIntegrationName);

        Get<TextBlock>("DirectIntegrationNameText").Text =
            descriptor.Name;

        Get<TextBlock>("DirectIntegrationSubtitleText").Text =
            descriptor.Summary;

        Get<TextBlock>("DirectIntegrationPrimaryTitleText").Text =
            descriptor.PrimaryModule;

        Get<TextBlock>("DirectIntegrationSecondaryTitleText").Text =
            descriptor.SecondaryModule;

        Get<Button>("DirectIntegrationOpenButton").Content =
            descriptor.OperationsLabel;

        if (integration is null)
        {
            Get<TextBlock>("DirectIntegrationStateText").Text =
                "NOT DETECTED";
            Get<TextBlock>("DirectIntegrationRuntimeText").Text =
                "--";
            Get<TextBlock>("DirectIntegrationEndpointText").Text =
                "--";
            Get<TextBlock>("DirectIntegrationRoleText").Text =
                IntegrationRole(descriptor.Name);
            Get<TextBlock>("DirectIntegrationFindingsText").Text =
                "0";
            Get<TextBlock>("DirectIntegrationOwnerText").Text =
                _controlPlane.ActiveProfile.DisplayName;
            Get<TextBlock>("DirectIntegrationEvidenceText").Text =
                "No verified runtime, service, container or published port was returned.";
            Get<TextBlock>("DirectIntegrationRelatedText").Text =
                "No operational context is available because this application was not detected.";
            Get<TextBlock>("DirectIntegrationOperationsText").Text =
                "Refresh the active target or review integration discovery.";
            Get<Button>("DirectIntegrationOpenButton").IsEnabled =
                false;
            Get<Button>("DirectIntegrationIntelligenceButton").IsEnabled =
                false;
            Get<Border>("DirectIntegrationStateBorder").Background =
                OpsPalette.Background(OpsSeverity.Info);
            Get<TextBlock>("DirectIntegrationStateText").Foreground =
                OpsPalette.Foreground(OpsSeverity.Info);
            return;
        }

        var related =
            _policyEvaluation?.Active
                .Where(item =>
                    MatchesIntegration(
                        item,
                        integration.Name))
                .ToArray() ??
            Array.Empty<OpsPolicyFinding>();

        var url =
            ResolveIntegrationUrl(
                integration);

        Get<TextBlock>("DirectIntegrationStateText").Text =
            LinuxOpsAnalyzer.SeverityLabel(
                integration.Severity);

        Get<TextBlock>("DirectIntegrationStateText").Foreground =
            OpsPalette.Foreground(
                integration.Severity);

        Get<Border>("DirectIntegrationStateBorder").Background =
            OpsPalette.Background(
                integration.Severity);

        Get<TextBlock>("DirectIntegrationRuntimeText").Text =
            integration.Kind;

        Get<TextBlock>("DirectIntegrationEndpointText").Text =
            url ??
            (string.IsNullOrWhiteSpace(
                integration.Endpoint)
                ? "No verified endpoint"
                : integration.Endpoint);

        Get<TextBlock>("DirectIntegrationRoleText").Text =
            IntegrationRole(
                integration.Name);

        Get<TextBlock>("DirectIntegrationFindingsText").Text =
            related.Length.ToString();

        Get<TextBlock>("DirectIntegrationOwnerText").Text =
            _controlPlane.ActiveProfile.DisplayName;

        Get<TextBlock>("DirectIntegrationEvidenceText").Text =
            IntegrationEvidenceSummary(
                integration);

        Get<TextBlock>("DirectIntegrationRelatedText").Text =
            related.Length == 0
                ? "No active operational finding is associated with this application."
                : string.Join(
                    Environment.NewLine +
                    Environment.NewLine,
                    related.Select(item =>
                        $"{item.Severity} · {item.Problem}" +
                        (string.IsNullOrWhiteSpace(
                            item.NextStep)
                            ? string.Empty
                            : Environment.NewLine +
                              $"Next · {item.NextStep}")));

        Get<TextBlock>("DirectIntegrationOperationsText").Text =
            url is null
                ? "No verified web endpoint is available. Logs, Docker and Intelligence remain available."
                : $"Ready · {url}";

        Get<Button>("DirectIntegrationOpenButton").IsEnabled =
            url is not null;

        Get<Button>("DirectIntegrationIntelligenceButton").IsEnabled =
            related.Length > 0;
    }

    private async void DirectIntegrationOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var integration =
            ActiveDirectIntegration();

        if (integration is null)
            return;

        var url =
            ResolveIntegrationUrl(
                integration);

        if (url is null)
            return;

        try
        {
            using var process =
                new Process
                {
                    StartInfo =
                        new ProcessStartInfo
                        {
                            FileName = "xdg-open",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                };

            process.StartInfo.ArgumentList.Add(
                url);

            process.Start();

            Get<TextBlock>("DirectIntegrationOperationsText").Text =
                $"Opened {url}";

            await Task.CompletedTask;
        }
        catch (Exception exception)
        {
            Get<TextBlock>("DirectIntegrationOperationsText").Text =
                $"Could not open interface: {exception.Message}";
        }
    }

    private void DirectIntegrationDockerButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("DockerNav");

    private void DirectIntegrationLogsButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("LogsNav");

    private void DirectIntegrationIntelligenceButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("IntelligenceNav");
}
