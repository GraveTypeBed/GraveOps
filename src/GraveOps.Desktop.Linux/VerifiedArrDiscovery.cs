using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GraveOps.Core.Hosts;

namespace GraveOps.Desktop.Linux;

public static class ApplicationVerificationStates
{
    public const string Verified = "Verified";
    public const string AuthenticationRequired = "Authentication required";
    public const string Unreachable = "Unreachable";
    public const string WrongProduct = "Wrong product";
    public const string UnsupportedApi = "Unsupported API version";
    public const string TlsFailed = "TLS validation failed";
    public const string RedirectBlocked = "Redirect blocked";
    public const string ConfigurationUnreadable = "Configuration unreadable";
    public const string ConflictingIdentity = "Conflicting identity";
    public const string Candidate = "Unverified candidate";
    public const string UnsafeDestination = "Manual approval required";
}

internal sealed record ArrStatusFingerprint(
    string Product,
    string InstanceName,
    string Version,
    string AppData,
    string StartupPath,
    string UrlBase,
    string ApiVersion);

internal sealed record ArrConfigCredential(
    string Product,
    string ApiKey,
    int ApplicationPort,
    bool UseSsl,
    string UrlBase,
    string InstanceName,
    string Source,
    string ContainerName,
    string Endpoint);

internal sealed record ArrProbeOutcome(
    bool Success,
    string State,
    string Detail,
    string ProbeUrl,
    string LaunchUrl,
    ArrStatusFingerprint? Fingerprint,
    ArrConfigCredential? Credential);

internal sealed record DockerPortMap(
    string ContainerName,
    IReadOnlyDictionary<int, IReadOnlyList<int>> ContainerToHostPorts);

internal sealed record ProwlarrTargetHint(
    string Product,
    string Name,
    string Url,
    string ApiKey);

internal sealed record ArrVerificationTestResult(
    bool Success,
    string State,
    string Product,
    string InstanceName,
    string ApiVersion);

public static class VerifiedArrDiscoveryService
{
    private const int MaxResponseCharacters = 262_144;
    private const int MaxDockerConfigFiles = 80;
    private const int MaximumConcurrentProbes = 4;

    private static readonly string[] SupportedProducts =
    {
        "Sonarr",
        "Radarr",
        "Lidarr",
        "Prowlarr",
        "Readarr",
        "Whisparr"
    };

    private static readonly IReadOnlyDictionary<string, string[]>
        ApiPaths =
            new Dictionary<string, string[]>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Sonarr"] =
                    new[]
                    {
                        "api/v5/system/status",
                        "api/v3/system/status"
                    },
                ["Radarr"] =
                    new[]
                    {
                        "api/v3/system/status"
                    },
                ["Lidarr"] =
                    new[]
                    {
                        "api/v1/system/status"
                    },
                ["Prowlarr"] =
                    new[]
                    {
                        "api/v1/system/status"
                    },
                ["Readarr"] =
                    new[]
                    {
                        "api/v1/system/status"
                    },
                ["Whisparr"] =
                    new[]
                    {
                        "api/v1/system/status"
                    }
            };

    private static readonly HttpClient Client =
        CreateClient();

    public static async Task<List<ApplicationIdentityRecord>>
        PromoteAsync(
            HostSnapshot snapshot,
            IReadOnlyList<ApplicationIdentityRecord> detected,
            string hostScope,
            string urlHost,
            bool inspectLocalDocker,
            ApplicationIdentityStore store,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(detected);
        ArgumentNullException.ThrowIfNull(store);

        var records =
            detected.ToList();

        var credentials =
            inspectLocalDocker
                ? await DiscoverCredentialsAsync(
                    snapshot,
                    urlHost,
                    cancellationToken)
                : new List<ArrConfigCredential>();

        AddConfigDerivedCandidates(
            records,
            credentials,
            hostScope);

        var firstPass =
            await VerifyRecordsAsync(
                records,
                credentials,
                hostScope,
                store,
                cancellationToken);

        records =
            firstPass.Records;

        var prowlarrHints =
            await DiscoverProwlarrTargetsAsync(
                firstPass.Outcomes,
                cancellationToken);

        if (prowlarrHints.Count > 0)
        {
            AddProwlarrCandidates(
                records,
                credentials,
                prowlarrHints,
                hostScope);

            var secondPass =
                await VerifyRecordsAsync(
                    records,
                    credentials,
                    hostScope,
                    store,
                    cancellationToken);

            records =
                secondPass.Records;
        }

        return Consolidate(records);
    }

    internal static IReadOnlyList<string>
        ApiPathsForTesting(
            string product) =>
        OrderedApiPaths(product);

    internal static ArrStatusFingerprint?
        ParseStatusForTesting(
            string json,
            string apiPath) =>
        ParseFingerprint(json, apiPath);

    internal static ArrConfigCredential?
        ParseConfigForTesting(
            string xml,
            string product,
            string endpoint) =>
        ParseConfig(
            xml,
            product,
            "fixture",
            string.Empty,
            endpoint);

    internal static async Task<bool>
        IsAutomaticEndpointAllowedForTestingAsync(
            string url,
            CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri))
        {
            return false;
        }

        return await IsSafeAutomaticEndpointAsync(
            uri,
            cancellationToken);
    }

    internal static async Task<ArrVerificationTestResult>
        VerifyForTestingAsync(
            string expectedProduct,
            string url,
            string apiKey,
            CancellationToken cancellationToken = default)
    {
        var record =
            new ApplicationIdentityRecord
            {
                SourceKey = "fixture|source",
                Product = expectedProduct,
                DisplayName = expectedProduct,
                Category = "Acquisition",
                Role = ApplicationIdentityRoles.DiscoveryCandidate,
                Protocol = "Fixture",
                ParentSourceKey = string.Empty,
                ParentProductHint = string.Empty,
                Kind = "Fixture",
                State = "running",
                Evidence = "fixture",
                Endpoint = url,
                Severity = OpsSeverity.Info,
                OwnsHealth = false,
                IsVerified = false,
                IsVisible = false,
                ShowInNavigation = false,
                Confidence = 20
            };

        var credential =
            new ArrConfigCredential(
                expectedProduct,
                apiKey,
                0,
                false,
                string.Empty,
                string.Empty,
                "fixture",
                string.Empty,
                url);

        var outcome =
            await ProbeRecordAsync(
                record,
                profile: null,
                new[] { credential },
                cancellationToken);

        return new ArrVerificationTestResult(
            outcome.Success,
            outcome.State,
            outcome.Fingerprint?.Product ??
            string.Empty,
            outcome.Fingerprint?.InstanceName ??
            string.Empty,
            outcome.Fingerprint?.ApiVersion ??
            string.Empty);
    }

    internal static ApplicationIdentityRecord
        ApplyFailedOutcomeForTesting(
            ApplicationIdentityRecord verified,
            string state,
            string detail) =>
        ApplyOutcome(
            verified,
            new ArrProbeOutcome(
                false,
                state,
                detail,
                verified.ProbeUrl,
                verified.LaunchUrl,
                null,
                null),
            "fixture",
            new[] { verified },
            null);

    internal static string StableVerifiedKeyForTesting(
        string hostScope,
        string parent,
        ArrStatusFingerprint fingerprint,
        string launchUrl) =>
        StableVerifiedKey(
            hostScope,
            parent,
            fingerprint,
            launchUrl);

    private static HttpClient CreateClient()
    {
        var handler =
            new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout =
                    TimeSpan.FromSeconds(3),
                PooledConnectionLifetime =
                    TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout =
                    TimeSpan.FromMinutes(1),
                MaxConnectionsPerServer =
                    MaximumConcurrentProbes
            };

        var client =
            new HttpClient(
                handler,
                disposeHandler: true)
            {
                Timeout =
                    TimeSpan.FromSeconds(6)
            };

        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "GraveOps",
                "4.9.0-G"));

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        return client;
    }

    private static async Task<(
        List<ApplicationIdentityRecord> Records,
        IReadOnlyList<ArrProbeOutcome> Outcomes)>
        VerifyRecordsAsync(
            IReadOnlyList<ApplicationIdentityRecord> source,
            IReadOnlyList<ArrConfigCredential> credentials,
            string hostScope,
            ApplicationIdentityStore store,
            CancellationToken cancellationToken)
    {
        var targets =
            source
                .Where(IsArrProductRecord)
                .Where(item =>
                    TryResolveProbeEndpoint(
                        item,
                        store.Get(item.SourceKey),
                        out _))
                .ToArray();

        var semaphore =
            new SemaphoreSlim(
                MaximumConcurrentProbes,
                MaximumConcurrentProbes);

        var tasks =
            targets.Select(
                async record =>
                {
                    await semaphore.WaitAsync(
                        cancellationToken);

                    try
                    {
                        var profile =
                            store.Get(
                                record.SourceKey);
                        var matching =
                            MatchingCredentials(
                                record,
                                credentials,
                                profile);

                        var outcome =
                            await ProbeRecordAsync(
                                record,
                                profile,
                                matching,
                                cancellationToken);

                        return (
                            Record: record,
                            Outcome: outcome);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                })
                .ToArray();

        var completed =
            tasks.Length == 0
                ? Array.Empty<(
                    ApplicationIdentityRecord Record,
                    ArrProbeOutcome Outcome)>()
                : await Task.WhenAll(tasks);

        var replacementBySource =
            new Dictionary<string, ApplicationIdentityRecord>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var item in completed)
        {
            replacementBySource[item.Record.SourceKey] =
                ApplyOutcome(
                    item.Record,
                    item.Outcome,
                    hostScope,
                    source,
                    store.Get(item.Record.SourceKey));
        }

        var records =
            source
                .Select(item =>
                    replacementBySource.TryGetValue(
                        item.SourceKey,
                        out var replacement)
                        ? replacement
                        : item)
                .ToList();

        return (
            records,
            completed
                .Select(item => item.Outcome)
                .ToArray());
    }

    private static ApplicationIdentityRecord
        ApplyOutcome(
            ApplicationIdentityRecord detected,
            ArrProbeOutcome outcome,
            string hostScope,
            IReadOnlyList<ApplicationIdentityRecord> allRecords,
            ApplicationIdentityProfile? profile)
    {
        var now =
            DateTimeOffset.Now;

        if (!outcome.Success ||
            outcome.Fingerprint is null)
        {
            if (detected.IsVerified &&
                detected.VerificationState.Equals(
                    ApplicationVerificationStates.Verified,
                    StringComparison.OrdinalIgnoreCase))
            {
                return detected with
                {
                    LastVerificationAt =
                        now,
                    VerificationDetail =
                        PreserveVerifiedDetail(
                            detected.VerificationDetail,
                            outcome.State,
                            outcome.Detail)
                };
            }

            return detected with
            {
                VerificationState =
                    outcome.State,
                VerificationDetail =
                    outcome.Detail,
                ProbeUrl =
                    outcome.ProbeUrl,
                LastVerificationAt =
                    now
            };
        }

        var fingerprint =
            outcome.Fingerprint;
        var operatorConfirmed =
            profile?.Confirmed == true;

        if (operatorConfirmed &&
            !detected.Product.Equals(
                fingerprint.Product,
                StringComparison.OrdinalIgnoreCase))
        {
            return detected with
            {
                VerificationState =
                    ApplicationVerificationStates
                        .ConflictingIdentity,
                VerificationDetail =
                    $"Endpoint returned {fingerprint.Product}, " +
                    $"but the operator-confirmed type is " +
                    $"{detected.Product}.",
                ProbeUrl =
                    outcome.ProbeUrl,
                LaunchUrl =
                    outcome.LaunchUrl,
                LastVerificationAt =
                    now,
                ApplicationVersion =
                    fingerprint.Version,
                ApiVersion =
                    fingerprint.ApiVersion,
                InstanceName =
                    fingerprint.InstanceName
            };
        }

        var parent =
            allRecords.FirstOrDefault(item =>
                item.SourceKey.Equals(
                    detected.ParentSourceKey,
                    StringComparison.OrdinalIgnoreCase));

        var role =
            operatorConfirmed
                ? detected.Role
                : parent is not null &&
                  !parent.Product.Equals(
                      fingerprint.Product,
                      StringComparison.OrdinalIgnoreCase)
                    ? ApplicationIdentityRoles
                        .EmbeddedApplication
                    : ApplicationIdentityRoles
                        .NativeApplication;

        var sourceKey =
            operatorConfirmed ||
            !IsHintSource(detected)
                ? detected.SourceKey
                : StableVerifiedKey(
                    hostScope,
                    detected.ParentSourceKey,
                    fingerprint,
                    outcome.LaunchUrl);

        var displayName =
            operatorConfirmed
                ? detected.DisplayName
                : BuildDisplayName(
                    fingerprint);

        var severity =
            RuntimeSeverity(
                detected.State,
                detected.Severity);

        var evidence =
            AppendEvidence(
                detected.Evidence,
                $"API verified {fingerprint.Product}" +
                (string.IsNullOrWhiteSpace(
                     fingerprint.InstanceName)
                    ? string.Empty
                    : $" instance {fingerprint.InstanceName}") +
                (string.IsNullOrWhiteSpace(
                     fingerprint.Version)
                    ? string.Empty
                    : $" version {fingerprint.Version}") +
                $" through {fingerprint.ApiVersion}");

        return detected with
        {
            SourceKey =
                sourceKey,
            Product =
                operatorConfirmed
                    ? detected.Product
                    : fingerprint.Product,
            DisplayName =
                displayName,
            Category =
                "Acquisition",
            Role =
                role,
            Protocol =
                $"Verified Arr {fingerprint.ApiVersion}",
            Kind =
                "Verified Arr API",
            Evidence =
                evidence,
            Endpoint =
                string.IsNullOrWhiteSpace(
                    outcome.LaunchUrl)
                    ? detected.Endpoint
                    : outcome.LaunchUrl,
            Severity =
                severity,
            OwnsHealth =
                ApplicationIdentityRoles
                    .CanOwnHealth(role),
            IsVerified =
                true,
            IsVisible =
                operatorConfirmed
                    ? detected.IsVisible
                    : true,
            ShowInNavigation =
                operatorConfirmed
                    ? detected.ShowInNavigation
                    : true,
            Confidence =
                Math.Max(
                    detected.Confidence,
                    105),
            VerificationState =
                ApplicationVerificationStates.Verified,
            VerificationDetail =
                "The application identified itself through " +
                "its read-only system status API.",
            ProbeUrl =
                outcome.ProbeUrl,
            LaunchUrl =
                outcome.LaunchUrl,
            LastVerificationAt =
                now,
            LastVerifiedAt =
                now,
            ApplicationVersion =
                fingerprint.Version,
            ApiVersion =
                fingerprint.ApiVersion,
            InstanceName =
                fingerprint.InstanceName,
            ApplicationDataPath =
                fingerprint.AppData,
            StartupPath =
                fingerprint.StartupPath
        };
    }

    private static bool IsHintSource(
        ApplicationIdentityRecord record) =>
        record.SourceKey.StartsWith(
            "hint|",
            StringComparison.OrdinalIgnoreCase) ||
        record.SourceKey.StartsWith(
            "candidate|",
            StringComparison.OrdinalIgnoreCase) ||
        record.Role.Equals(
            ApplicationIdentityRoles.DiscoveryCandidate,
            StringComparison.OrdinalIgnoreCase);

    private static string BuildDisplayName(
        ArrStatusFingerprint fingerprint)
    {
        var instance =
            fingerprint.InstanceName?.Trim() ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(instance) ||
            instance.Equals(
                fingerprint.Product,
                StringComparison.OrdinalIgnoreCase) ||
            instance.Equals(
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return fingerprint.Product;
        }

        return
            $"{fingerprint.Product} — {instance}";
    }

    private static OpsSeverity RuntimeSeverity(
        string state,
        OpsSeverity existing)
    {
        var value =
            state?.ToLowerInvariant() ??
            string.Empty;

        if (value.Contains("failed") ||
            value.Contains("unhealthy") ||
            value.Contains("error"))
        {
            return OpsSeverity.Error;
        }

        if (value.Contains("running") ||
            value.Contains("healthy") ||
            value.Contains("active") ||
            value.StartsWith(
                "up ",
                StringComparison.Ordinal))
        {
            return OpsSeverity.Healthy;
        }

        return existing == OpsSeverity.Info
            ? OpsSeverity.Healthy
            : existing;
    }

    private static IReadOnlyList<
        ArrConfigCredential>
        MatchingCredentials(
            ApplicationIdentityRecord record,
            IReadOnlyList<ArrConfigCredential> credentials,
            ApplicationIdentityProfile? profile)
    {
        var endpoint =
            profile?.UrlOverride;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint =
                !string.IsNullOrWhiteSpace(
                    record.ProbeUrl)
                    ? record.ProbeUrl
                    : record.Endpoint;
        }

        Uri.TryCreate(
            endpoint,
            UriKind.Absolute,
            out var target);

        return credentials
            .Where(item =>
                string.IsNullOrWhiteSpace(
                    item.Product) ||
                item.Product.Equals(
                    record.Product,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item =>
                EquivalentBase(
                    item.Endpoint,
                    endpoint))
            .ThenByDescending(item =>
                target is not null &&
                Uri.TryCreate(
                    item.Endpoint,
                    UriKind.Absolute,
                    out var credentialUri) &&
                credentialUri.Port ==
                target.Port)
            .ThenByDescending(item =>
                !string.IsNullOrWhiteSpace(
                    item.ApiKey))
            .Take(8)
            .ToArray();
    }

    private static async Task<ArrProbeOutcome>
        ProbeRecordAsync(
            ApplicationIdentityRecord record,
            ApplicationIdentityProfile? profile,
            IReadOnlyList<ArrConfigCredential> credentials,
            CancellationToken cancellationToken)
    {
        if (!TryResolveProbeEndpoint(
                record,
                profile,
                out var baseUri))
        {
            return new ArrProbeOutcome(
                false,
                ApplicationVerificationStates.Candidate,
                "No candidate probe URL is available.",
                string.Empty,
                string.Empty,
                null,
                null);
        }

        if (!await IsSafeAutomaticEndpointAsync(
                baseUri,
                cancellationToken))
        {
            return new ArrProbeOutcome(
                false,
                ApplicationVerificationStates.UnsafeDestination,
                "Automatic verification is limited to loopback " +
                "and private-network destinations. The URL remains " +
                "available for explicit operator confirmation.",
                baseUri.ToString(),
                baseUri.ToString(),
                null,
                null);
        }

        var keys =
            credentials
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.ApiKey))
                .GroupBy(item =>
                    item.ApiKey,
                    StringComparer.Ordinal)
                .Select(group =>
                    group.First())
                .Cast<ArrConfigCredential?>()
                .ToList();

        keys.Add(null);

        var sawAuthentication =
            false;
        var sawNotFound =
            false;
        var sawJson =
            false;

        foreach (var apiPath in
                 OrderedApiPaths(record.Product))
        {
            foreach (var credential in keys)
            {
                var requestUri =
                    Combine(
                        baseUri,
                        apiPath);

                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        requestUri);

                if (!string.IsNullOrWhiteSpace(
                        credential?.ApiKey))
                {
                    request.Headers.TryAddWithoutValidation(
                        "X-Api-Key",
                        credential.ApiKey);
                }

                try
                {
                    using var response =
                        await Client.SendAsync(
                            request,
                            HttpCompletionOption
                                .ResponseHeadersRead,
                            cancellationToken);

                    if (IsRedirect(response.StatusCode))
                    {
                        return new ArrProbeOutcome(
                            false,
                            ApplicationVerificationStates.RedirectBlocked,
                            "The endpoint returned a redirect. GraveOps " +
                            "does not follow redirects while credentials " +
                            "may be present.",
                            requestUri.ToString(),
                            baseUri.ToString(),
                            null,
                            null);
                    }

                    if (response.StatusCode is
                        HttpStatusCode.Unauthorized or
                        HttpStatusCode.Forbidden)
                    {
                        sawAuthentication =
                            true;
                        continue;
                    }

                    if (response.StatusCode ==
                        HttpStatusCode.NotFound)
                    {
                        sawNotFound =
                            true;
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                        continue;

                    var payload =
                        await response.Content
                            .ReadAsStringAsync(
                                cancellationToken);

                    if (payload.Length >
                        MaxResponseCharacters)
                    {
                        continue;
                    }

                    sawJson =
                        true;

                    var fingerprint =
                        ParseFingerprint(
                            payload,
                            apiPath);

                    if (fingerprint is null)
                        continue;

                    var launchBase =
                        ApplyReturnedUrlBase(
                            baseUri,
                            fingerprint.UrlBase);

                    return new ArrProbeOutcome(
                        true,
                        ApplicationVerificationStates.Verified,
                        "Application status fingerprint verified.",
                        requestUri.ToString(),
                        launchBase.ToString(),
                        fingerprint,
                        credential);
                }
                catch (HttpRequestException exception)
                    when (IsTlsFailure(exception))
                {
                    return new ArrProbeOutcome(
                        false,
                        ApplicationVerificationStates.TlsFailed,
                        "TLS validation failed. Automatic discovery " +
                        "does not bypass certificate validation.",
                        requestUri.ToString(),
                        baseUri.ToString(),
                        null,
                        null);
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    return new ArrProbeOutcome(
                        false,
                        ApplicationVerificationStates.Unreachable,
                        "The endpoint did not respond before the " +
                        "verification timeout.",
                        requestUri.ToString(),
                        baseUri.ToString(),
                        null,
                        null);
                }
                catch (HttpRequestException)
                {
                    continue;
                }
                catch (JsonException)
                {
                    continue;
                }
            }
        }

        var state =
            sawAuthentication
                ? ApplicationVerificationStates
                    .AuthenticationRequired
                : sawJson
                    ? ApplicationVerificationStates
                        .WrongProduct
                    : sawNotFound
                        ? ApplicationVerificationStates
                            .UnsupportedApi
                        : ApplicationVerificationStates
                            .Unreachable;

        var detail =
            state switch
            {
                ApplicationVerificationStates
                    .AuthenticationRequired =>
                    "The endpoint requires an API key and no " +
                    "matching readable credential was found.",
                ApplicationVerificationStates
                    .WrongProduct =>
                    "The endpoint responded, but did not return a " +
                    "supported Arr system-status fingerprint.",
                ApplicationVerificationStates
                    .UnsupportedApi =>
                    "The endpoint was reachable, but none of the " +
                    "supported Arr system-status API versions existed.",
                _ =>
                    "The endpoint could not be verified."
            };

        return new ArrProbeOutcome(
            false,
            state,
            detail,
            baseUri.ToString(),
            baseUri.ToString(),
            null,
            null);
    }

    private static IReadOnlyList<string>
        OrderedApiPaths(
            string expectedProduct)
    {
        var ordered =
            new List<string>();

        if (ApiPaths.TryGetValue(
                expectedProduct,
                out var preferred))
        {
            ordered.AddRange(preferred);
        }

        foreach (var path in
                 ApiPaths.Values.SelectMany(
                     value => value))
        {
            if (!ordered.Contains(
                    path,
                    StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(path);
            }
        }

        return ordered;
    }

    private static ArrStatusFingerprint?
        ParseFingerprint(
            string payload,
            string apiPath)
    {
        using var document =
            JsonDocument.Parse(payload);

        if (document.RootElement.ValueKind !=
            JsonValueKind.Object)
        {
            return null;
        }

        var root =
            document.RootElement;
        var appName =
            ReadString(
                root,
                "appName");

        var product =
            NormalizeProduct(appName);

        if (string.IsNullOrWhiteSpace(product))
            return null;

        return new ArrStatusFingerprint(
            product,
            ReadString(
                root,
                "instanceName"),
            ReadString(
                root,
                "version"),
            ReadString(
                root,
                "appData"),
            ReadString(
                root,
                "startupPath"),
            ReadString(
                root,
                "urlBase"),
            ApiVersionFromPath(apiPath));
    }

    private static string NormalizeProduct(
        string value)
    {
        foreach (var product in
                 SupportedProducts)
        {
            if (value.Equals(
                    product,
                    StringComparison.OrdinalIgnoreCase) ||
                value.Contains(
                    product,
                    StringComparison.OrdinalIgnoreCase))
            {
                return product;
            }
        }

        return string.Empty;
    }

    private static string ApiVersionFromPath(
        string path)
    {
        var match =
            System.Text.RegularExpressions.Regex.Match(
                path,
                @"/?api/(v\d+)/",
                System.Text.RegularExpressions
                    .RegexOptions.IgnoreCase);

        return match.Success
            ? match.Groups[1].Value.ToLowerInvariant()
            : "unknown API";
    }

    private static bool TryResolveProbeEndpoint(
        ApplicationIdentityRecord record,
        ApplicationIdentityProfile? profile,
        out Uri uri)
    {
        var value =
            profile?.UrlOverride;

        if (string.IsNullOrWhiteSpace(value))
        {
            value =
                !string.IsNullOrWhiteSpace(
                    record.ProbeUrl)
                    ? record.ProbeUrl
                    : record.Endpoint;
        }

        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp &&
             parsed.Scheme != Uri.UriSchemeHttps))
        {
            uri =
                null!;
            return false;
        }

        uri =
            NormalizeBaseUri(parsed);
        return true;
    }

    private static Uri NormalizeBaseUri(
        Uri uri)
    {
        var builder =
            new UriBuilder(uri)
            {
                Query = string.Empty,
                Fragment = string.Empty
            };

        var path =
            builder.Path
                .TrimEnd('/');

        builder.Path =
            string.IsNullOrWhiteSpace(path)
                ? "/"
                : path + "/";

        return builder.Uri;
    }

    private static Uri Combine(
        Uri baseUri,
        string relative)
    {
        var builder =
            new UriBuilder(baseUri);
        var prefix =
            builder.Path.TrimEnd('/');
        var suffix =
            relative.Trim('/');

        builder.Path =
            $"{prefix}/{suffix}";
        builder.Query =
            string.Empty;
        builder.Fragment =
            string.Empty;

        return builder.Uri;
    }

    private static Uri ApplyReturnedUrlBase(
        Uri baseUri,
        string returned)
    {
        if (string.IsNullOrWhiteSpace(returned))
            return NormalizeBaseUri(baseUri);

        var builder =
            new UriBuilder(baseUri);
        var current =
            builder.Path.TrimEnd('/');
        var normalized =
            "/" + returned.Trim('/');

        if (!current.EndsWith(
                normalized,
                StringComparison.OrdinalIgnoreCase))
        {
            builder.Path =
                normalized + "/";
        }

        return builder.Uri;
    }

    private static async Task<bool>
        IsSafeAutomaticEndpointAsync(
            Uri uri,
            CancellationToken cancellationToken)
    {
        if ((uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrWhiteSpace(
                uri.UserInfo) ||
            !string.IsNullOrWhiteSpace(
                uri.Fragment))
        {
            return false;
        }

        if (IPAddress.TryParse(
                uri.DnsSafeHost,
                out var parsed))
        {
            return IsPrivateOrLoopback(parsed);
        }

        if (uri.DnsSafeHost.Equals(
                "localhost",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var addresses =
                await Dns.GetHostAddressesAsync(
                    uri.DnsSafeHost,
                    cancellationToken);

            return addresses.Length > 0 &&
                   addresses.All(
                       IsPrivateOrLoopback);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPrivateOrLoopback(
        IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.IsIPv4MappedToIPv6)
        {
            address =
                address.MapToIPv4();
        }

        var bytes =
            address.GetAddressBytes();

        if (address.AddressFamily ==
            AddressFamily.InterNetwork)
        {
            if (bytes[0] == 10)
                return true;

            if (bytes[0] == 172 &&
                bytes[1] is >= 16 and <= 31)
            {
                return true;
            }

            if (bytes[0] == 192 &&
                bytes[1] == 168)
            {
                return true;
            }

            return false;
        }

        if (address.AddressFamily ==
            AddressFamily.InterNetworkV6)
        {
            return (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }

    private static bool IsRedirect(
        HttpStatusCode status) =>
        (int)status is >= 300 and <= 399;

    private static bool IsTlsFailure(
        HttpRequestException exception) =>
        exception.InnerException is
            System.Security.Authentication
                .AuthenticationException ||
        exception.Message.Contains(
            "SSL",
            StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains(
            "certificate",
            StringComparison.OrdinalIgnoreCase);

    private static async Task<List<
        ArrConfigCredential>>
        DiscoverCredentialsAsync(
            HostSnapshot snapshot,
            string urlHost,
            CancellationToken cancellationToken)
    {
        var result =
            new List<ArrConfigCredential>();

        DiscoverHostCredentials(
            result,
            urlHost);

        var systemdPaths =
            await DiscoverSystemdDataPathsAsync(
                snapshot.Services,
                cancellationToken);

        foreach (var item in systemdPaths)
        {
            TryReadHostConfig(
                result,
                item.Product,
                item.Path,
                urlHost,
                item.Source);
        }

        var docker =
            await DiscoverDockerCredentialsAsync(
                snapshot.Containers,
                urlHost,
                cancellationToken);

        result.AddRange(docker);

        return result
            .GroupBy(
                item =>
                    $"{item.Product}|{CanonicalUrl(item.Endpoint)}|" +
                    $"{SecretFingerprint(item.ApiKey)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                group.First())
            .ToList();
    }

    private static void DiscoverHostCredentials(
        ICollection<ArrConfigCredential> result,
        string urlHost)
    {
        var home =
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);

        var candidates =
            new Dictionary<string, string[]>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Sonarr"] =
                    new[]
                    {
                        Path.Combine(
                            home,
                            ".config",
                            "NzbDrone",
                            "config.xml"),
                        Path.Combine(
                            home,
                            ".config",
                            "Sonarr",
                            "config.xml"),
                        "/var/lib/sonarr/config.xml",
                        "/var/lib/nzbdrone/config.xml"
                    },
                ["Radarr"] =
                    new[]
                    {
                        Path.Combine(
                            home,
                            ".config",
                            "Radarr",
                            "config.xml"),
                        "/var/lib/radarr/config.xml"
                    },
                ["Lidarr"] =
                    new[]
                    {
                        Path.Combine(
                            home,
                            ".config",
                            "Lidarr",
                            "config.xml"),
                        "/var/lib/lidarr/config.xml"
                    },
                ["Prowlarr"] =
                    new[]
                    {
                        Path.Combine(
                            home,
                            ".config",
                            "Prowlarr",
                            "config.xml"),
                        "/var/lib/prowlarr/config.xml"
                    },
                ["Readarr"] =
                    new[]
                    {
                        Path.Combine(
                            home,
                            ".config",
                            "Readarr",
                            "config.xml"),
                        "/var/lib/readarr/config.xml"
                    },
                ["Whisparr"] =
                    new[]
                    {
                        Path.Combine(
                            home,
                            ".config",
                            "Whisparr",
                            "config.xml"),
                        "/var/lib/whisparr/config.xml"
                    }
            };

        foreach (var pair in candidates)
        {
            foreach (var path in pair.Value)
            {
                TryReadHostConfig(
                    result,
                    pair.Key,
                    path,
                    urlHost,
                    $"host config {path}");
            }
        }
    }

    private static void TryReadHostConfig(
        ICollection<ArrConfigCredential> result,
        string product,
        string path,
        string urlHost,
        string source)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var parsed =
                ParseConfig(
                    File.ReadAllText(path),
                    product,
                    source,
                    string.Empty,
                    string.Empty);

            if (parsed is null)
                return;

            var endpoint =
                BuildEndpoint(
                    parsed,
                    urlHost,
                    parsed.ApplicationPort);

            result.Add(
                parsed with
                {
                    Endpoint =
                        endpoint
                });
        }
        catch
        {
            // Unreadable configs remain undisclosed and the source
            // remains a candidate.
        }
    }

    private static async Task<List<(
        string Product,
        string Path,
        string Source)>>
        DiscoverSystemdDataPathsAsync(
            IReadOnlyList<ServiceSnapshot> services,
            CancellationToken cancellationToken)
    {
        var result =
            new List<(
                string Product,
                string Path,
                string Source)>();

        foreach (var service in services)
        {
            var product =
                ProductFromText(
                    service.Unit);

            if (string.IsNullOrWhiteSpace(product))
                continue;

            var command =
                await RunProcessAsync(
                    "systemctl",
                    new[]
                    {
                        "show",
                        service.Unit,
                        "--property=ExecStart",
                        "--value"
                    },
                    TimeSpan.FromSeconds(3),
                    cancellationToken);

            if (!command.Success)
                continue;

            foreach (var dataPath in
                     ParseDataPaths(
                         command.Output))
            {
                result.Add(
                    (
                        product,
                        Path.Combine(
                            dataPath,
                            "config.xml"),
                        $"systemd {service.Unit}"
                    ));
            }
        }

        return result;
    }

    private static IEnumerable<string>
        ParseDataPaths(
            string execStart)
    {
        var matches =
            System.Text.RegularExpressions.Regex
                .Matches(
                    execStart ?? string.Empty,
                    @"(?:--?data(?:=|\s+))" +
                    @"(?:""([^""]+)""|'([^']+)'|([^\s;]+))",
                    System.Text.RegularExpressions
                        .RegexOptions.IgnoreCase);

        foreach (
            System.Text.RegularExpressions.Match
                match in matches)
        {
            var value =
                match.Groups
                    .Cast<
                        System.Text.RegularExpressions
                            .Group>()
                    .Skip(1)
                    .FirstOrDefault(group =>
                        group.Success)
                    ?.Value;

            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }
    }

    private static async Task<List<
        ArrConfigCredential>>
        DiscoverDockerCredentialsAsync(
            IReadOnlyList<DockerContainerSnapshot> containers,
            string urlHost,
            CancellationToken cancellationToken)
    {
        var result =
            new List<ArrConfigCredential>();

        foreach (var container in containers)
        {
            if (!SafeContainerName(
                    container.Name))
            {
                continue;
            }

            var map =
                await InspectDockerPortsAsync(
                    container.Name,
                    cancellationToken);

            var find =
                await RunProcessAsync(
                    "docker",
                    new[]
                    {
                        "exec",
                        container.Name,
                        "find",
                        "/config",
                        "/data",
                        "/app",
                        "/opt",
                        "/var/lib",
                        "/home",
                        "/sonarr",
                        "/radarr",
                        "/lidarr",
                        "/prowlarr",
                        "/readarr",
                        "/whisparr",
                        "-maxdepth",
                        "6",
                        "-type",
                        "f",
                        "-iname",
                        "config.xml"
                    },
                    TimeSpan.FromSeconds(8),
                    cancellationToken,
                    acceptNonZeroWithOutput: true);

            if (string.IsNullOrWhiteSpace(
                    find.Output))
            {
                continue;
            }

            var paths =
                find.Output
                    .Split(
                        '\n',
                        StringSplitOptions
                            .RemoveEmptyEntries |
                        StringSplitOptions
                            .TrimEntries)
                    .Where(path =>
                        path.StartsWith(
                            "/",
                            StringComparison.Ordinal))
                    .Distinct(
                        StringComparer.Ordinal)
                    .Take(
                        MaxDockerConfigFiles)
                    .ToArray();

            foreach (var path in paths)
            {
                var cat =
                    await RunProcessAsync(
                        "docker",
                        new[]
                        {
                            "exec",
                            container.Name,
                            "cat",
                            path
                        },
                        TimeSpan.FromSeconds(4),
                        cancellationToken);

                if (!cat.Success ||
                    string.IsNullOrWhiteSpace(
                        cat.Output))
                {
                    continue;
                }

                var product =
                    ProductFromText(
                        path + " " +
                        container.Name + " " +
                        container.Image);

                var parsed =
                    ParseConfig(
                        cat.Output,
                        product,
                        $"container {container.Name}:{path}",
                        container.Name,
                        string.Empty);

                if (parsed is null)
                    continue;

                var hostPorts =
                    map.ContainerToHostPorts
                        .TryGetValue(
                            parsed.ApplicationPort,
                            out var values)
                        ? values
                        : Array.Empty<int>();

                foreach (var hostPort in hostPorts)
                {
                    result.Add(
                        parsed with
                        {
                            Endpoint =
                                BuildEndpoint(
                                    parsed,
                                    urlHost,
                                    hostPort)
                        });
                }
            }
        }

        return result;
    }

    private static async Task<DockerPortMap>
        InspectDockerPortsAsync(
            string containerName,
            CancellationToken cancellationToken)
    {
        var result =
            new Dictionary<int, IReadOnlyList<int>>();

        var inspect =
            await RunProcessAsync(
                "docker",
                new[]
                {
                    "inspect",
                    containerName
                },
                TimeSpan.FromSeconds(5),
                cancellationToken);

        if (!inspect.Success)
        {
            return new DockerPortMap(
                containerName,
                result);
        }

        try
        {
            using var document =
                JsonDocument.Parse(
                    inspect.Output);

            var root =
                document.RootElement[0];
            var network =
                root.GetProperty(
                    "NetworkSettings");
            var ports =
                network.GetProperty(
                    "Ports");

            foreach (var item in
                     ports.EnumerateObject())
            {
                var slash =
                    item.Name.IndexOf('/');

                if (slash <= 0 ||
                    !int.TryParse(
                        item.Name[..slash],
                        out var containerPort) ||
                    item.Value.ValueKind !=
                        JsonValueKind.Array)
                {
                    continue;
                }

                var hostPorts =
                    item.Value
                        .EnumerateArray()
                        .Select(binding =>
                            binding.TryGetProperty(
                                "HostPort",
                                out var value)
                                ? value.GetString()
                                : null)
                        .Where(value =>
                            int.TryParse(
                                value,
                                out _))
                        .Select(value =>
                            int.Parse(
                                value!))
                        .Distinct()
                        .OrderBy(port => port)
                        .ToArray();

                if (hostPorts.Length > 0)
                {
                    result[containerPort] =
                        hostPorts;
                }
            }
        }
        catch
        {
            // Missing or malformed Docker metadata yields no mapping.
        }

        return new DockerPortMap(
            containerName,
            result);
    }

    private static ArrConfigCredential?
        ParseConfig(
            string xml,
            string productHint,
            string source,
            string containerName,
            string endpoint)
    {
        try
        {
            var document =
                XDocument.Parse(
                    xml,
                    LoadOptions.None);
            var root =
                document.Root;

            if (root is null)
                return null;

            string Value(string name) =>
                root.Elements()
                    .FirstOrDefault(element =>
                        element.Name.LocalName.Equals(
                            name,
                            StringComparison.OrdinalIgnoreCase))
                    ?.Value
                    ?.Trim() ??
                string.Empty;

            var port =
                int.TryParse(
                    Value("Port"),
                    out var parsedPort)
                    ? parsedPort
                    : 0;
            var sslPort =
                int.TryParse(
                    Value("SslPort"),
                    out var parsedSslPort)
                    ? parsedSslPort
                    : 0;
            var enableSsl =
                bool.TryParse(
                    Value("EnableSsl"),
                    out var ssl) &&
                ssl;
            var applicationPort =
                enableSsl && sslPort > 0
                    ? sslPort
                    : port;

            if (applicationPort is <= 0 or > 65535)
                return null;

            var product =
                string.IsNullOrWhiteSpace(
                    productHint)
                    ? ProductFromText(
                        source)
                    : productHint;

            return new ArrConfigCredential(
                product,
                Value("ApiKey"),
                applicationPort,
                enableSsl,
                NormalizeUrlBase(
                    Value("UrlBase")),
                Value("InstanceName"),
                source,
                containerName,
                endpoint);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildEndpoint(
        ArrConfigCredential credential,
        string urlHost,
        int hostPort)
    {
        var suffix =
            string.IsNullOrWhiteSpace(
                credential.UrlBase)
                ? string.Empty
                : credential.UrlBase;

        var scheme =
            credential.UseSsl
                ? "https"
                : "http";

        return
            $"{scheme}://{urlHost}:{hostPort}{suffix}";
    }

    private static string NormalizeUrlBase(
        string value)
    {
        var normalized =
            value?.Trim().Trim('/') ??
            string.Empty;

        return string.IsNullOrWhiteSpace(
            normalized)
            ? string.Empty
            : "/" + normalized;
    }

    private static void AddConfigDerivedCandidates(
        List<ApplicationIdentityRecord> records,
        IReadOnlyList<ArrConfigCredential> credentials,
        string hostScope)
    {
        foreach (var credential in credentials)
        {
            if (string.IsNullOrWhiteSpace(
                    credential.Product) ||
                !SupportedProducts.Contains(
                    credential.Product,
                    StringComparer.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(
                    credential.Endpoint))
            {
                continue;
            }

            var existing =
                records.FirstOrDefault(item =>
                    item.Product.Equals(
                        credential.Product,
                        StringComparison.OrdinalIgnoreCase) &&
                    EquivalentBase(
                        item.Endpoint,
                        credential.Endpoint));

            if (existing is not null)
                continue;

            var owner =
                string.IsNullOrWhiteSpace(
                    credential.ContainerName)
                    ? null
                    : records.FirstOrDefault(item =>
                        item.Evidence.Contains(
                            credential.ContainerName,
                            StringComparison.OrdinalIgnoreCase) &&
                        ApplicationIdentityRoles.IsTopLevel(
                            item.Role));

            if (owner is not null &&
                owner.Product.Equals(
                    credential.Product,
                    StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(
                    owner.Endpoint))
            {
                var index =
                    records.IndexOf(owner);

                records[index] =
                    owner with
                    {
                        Endpoint =
                            credential.Endpoint,
                        ProbeUrl =
                            credential.Endpoint,
                        VerificationState =
                            ApplicationVerificationStates
                                .Candidate,
                        VerificationDetail =
                            "A readable application configuration " +
                            "provided a candidate endpoint."
                    };

                continue;
            }

            records.Add(
                new ApplicationIdentityRecord
                {
                    SourceKey =
                        CandidateKey(
                            hostScope,
                            credential.Product,
                            credential.Endpoint,
                            owner?.SourceKey ??
                            string.Empty),
                    Product =
                        credential.Product,
                    DisplayName =
                        $"{credential.Product} candidate",
                    Category =
                        "Acquisition",
                    Role =
                        ApplicationIdentityRoles
                            .DiscoveryCandidate,
                    Protocol =
                        "Unverified config-derived endpoint",
                    ParentSourceKey =
                        owner?.Product.Equals(
                            "DUMB",
                            StringComparison.OrdinalIgnoreCase) ==
                        true
                            ? owner.SourceKey
                            : string.Empty,
                    ParentProductHint =
                        owner?.Product ??
                        string.Empty,
                    Kind =
                        "Configuration hint",
                    State =
                        owner?.State ??
                        "Candidate",
                    Evidence =
                        "A readable Arr configuration supplied a " +
                        "candidate endpoint. API keys remain transient.",
                    Endpoint =
                        credential.Endpoint,
                    Severity =
                        OpsSeverity.Info,
                    OwnsHealth =
                        false,
                    IsVerified =
                        false,
                    IsVisible =
                        false,
                    ShowInNavigation =
                        false,
                    Confidence =
                        45,
                    VerificationState =
                        ApplicationVerificationStates.Candidate,
                    VerificationDetail =
                        "Awaiting direct API verification.",
                    ProbeUrl =
                        credential.Endpoint
                });
        }
    }

    private static async Task<List<
        ProwlarrTargetHint>>
        DiscoverProwlarrTargetsAsync(
            IReadOnlyList<ArrProbeOutcome> outcomes,
            CancellationToken cancellationToken)
    {
        var result =
            new List<ProwlarrTargetHint>();

        foreach (var outcome in outcomes)
        {
            if (!outcome.Success ||
                outcome.Fingerprint?.Product.Equals(
                    "Prowlarr",
                    StringComparison.OrdinalIgnoreCase) !=
                true ||
                string.IsNullOrWhiteSpace(
                    outcome.Credential?.ApiKey) ||
                !Uri.TryCreate(
                    outcome.LaunchUrl,
                    UriKind.Absolute,
                    out var baseUri))
            {
                continue;
            }

            var applicationsUri =
                Combine(
                    baseUri,
                    "api/v1/applications");

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    applicationsUri);

            request.Headers.TryAddWithoutValidation(
                "X-Api-Key",
                outcome.Credential.ApiKey);

            try
            {
                using var response =
                    await Client.SendAsync(
                        request,
                        HttpCompletionOption
                            .ResponseHeadersRead,
                        cancellationToken);

                if (IsRedirect(response.StatusCode) ||
                    !response.IsSuccessStatusCode)
                {
                    continue;
                }

                var payload =
                    await response.Content
                        .ReadAsStringAsync(
                            cancellationToken);

                if (payload.Length >
                    MaxResponseCharacters)
                {
                    continue;
                }

                using var document =
                    JsonDocument.Parse(payload);

                if (document.RootElement.ValueKind !=
                    JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var application in
                         document.RootElement
                             .EnumerateArray())
                {
                    var implementation =
                        ReadString(
                            application,
                            "implementation");
                    var product =
                        ProductFromText(
                            implementation);

                    if (string.IsNullOrWhiteSpace(
                            product))
                    {
                        continue;
                    }

                    var url =
                        ReadFieldValue(
                            application,
                            "baseUrl",
                            "url");
                    var apiKey =
                        ReadFieldValue(
                            application,
                            "apiKey");

                    if (string.IsNullOrWhiteSpace(url) ||
                        !Uri.TryCreate(
                            url,
                            UriKind.Absolute,
                            out _))
                    {
                        continue;
                    }

                    result.Add(
                        new ProwlarrTargetHint(
                            product,
                            ReadString(
                                application,
                                "name"),
                            url,
                            apiKey));
                }
            }
            catch
            {
                // Prowlarr assistance is optional and never affects
                // an already verified application.
            }
        }

        return result
            .GroupBy(
                item =>
                    $"{item.Product}|{CanonicalUrl(item.Url)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                group.First())
            .ToList();
    }

    private static void AddProwlarrCandidates(
        List<ApplicationIdentityRecord> records,
        List<ArrConfigCredential> credentials,
        IReadOnlyList<ProwlarrTargetHint> hints,
        string hostScope)
    {
        foreach (var hint in hints)
        {
            if (records.Any(item =>
                    item.Product.Equals(
                        hint.Product,
                        StringComparison.OrdinalIgnoreCase) &&
                    EquivalentBase(
                        item.Endpoint,
                        hint.Url)))
            {
                continue;
            }

            credentials.Add(
                new ArrConfigCredential(
                    hint.Product,
                    hint.ApiKey,
                    0,
                    Uri.TryCreate(
                        hint.Url,
                        UriKind.Absolute,
                        out var hintUri) &&
                    hintUri.Scheme ==
                        Uri.UriSchemeHttps,
                    string.Empty,
                    hint.Name,
                    "Prowlarr application registry",
                    string.Empty,
                    hint.Url));

            records.Add(
                new ApplicationIdentityRecord
                {
                    SourceKey =
                        CandidateKey(
                            hostScope,
                            hint.Product,
                            hint.Url,
                            "prowlarr"),
                    Product =
                        hint.Product,
                    DisplayName =
                        string.IsNullOrWhiteSpace(
                            hint.Name)
                            ? $"{hint.Product} candidate"
                            : hint.Name,
                    Category =
                        "Acquisition",
                    Role =
                        ApplicationIdentityRoles
                            .DiscoveryCandidate,
                    Protocol =
                        "Prowlarr-supplied endpoint hint",
                    ParentSourceKey =
                        string.Empty,
                    ParentProductHint =
                        string.Empty,
                    Kind =
                        "Prowlarr application registry",
                    State =
                        "Candidate",
                    Evidence =
                        "Prowlarr supplied a target URL. The target " +
                        "must identify itself before promotion.",
                    Endpoint =
                        hint.Url,
                    Severity =
                        OpsSeverity.Info,
                    OwnsHealth =
                        false,
                    IsVerified =
                        false,
                    IsVisible =
                        false,
                    ShowInNavigation =
                        false,
                    Confidence =
                        50,
                    VerificationState =
                        ApplicationVerificationStates.Candidate,
                    VerificationDetail =
                        "Awaiting direct target verification.",
                    ProbeUrl =
                        hint.Url
                });
        }
    }

    private static List<ApplicationIdentityRecord>
        Consolidate(
            IReadOnlyList<ApplicationIdentityRecord> records)
    {
        var grouped =
            records
                .GroupBy(
                    item => item.SourceKey,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderByDescending(item =>
                            item.IsVerified)
                        .ThenByDescending(item =>
                            item.Confidence)
                        .First())
                .ToList();

        var verified =
            grouped
                .Where(item =>
                    item.IsVerified &&
                    IsArrProductRecord(item))
                .ToArray();

        return grouped
            .Where(item =>
            {
                if (item.IsVerified ||
                    !IsArrProductRecord(item))
                {
                    return true;
                }

                return !verified.Any(match =>
                    match.Product.Equals(
                        item.Product,
                        StringComparison.OrdinalIgnoreCase) &&
                    (
                        EquivalentBase(
                            match.Endpoint,
                            item.Endpoint) ||
                        (
                            !string.IsNullOrWhiteSpace(
                                match.ParentSourceKey) &&
                            match.ParentSourceKey.Equals(
                                item.ParentSourceKey,
                                StringComparison.OrdinalIgnoreCase) &&
                            SamePort(
                                match.Endpoint,
                                item.Endpoint)
                        )
                    ));
            })
            .OrderBy(item =>
                item.Category)
            .ThenBy(item =>
                item.Product)
            .ThenBy(item =>
                item.DisplayName)
            .ThenBy(item =>
                item.SourceKey)
            .ToList();
    }

    private static bool IsArrProductRecord(
        ApplicationIdentityRecord record) =>
        SupportedProducts.Contains(
            record.Product,
            StringComparer.OrdinalIgnoreCase);

    private static string ProductFromText(
        string text)
    {
        foreach (var product in
                 SupportedProducts)
        {
            if (text.Contains(
                    product,
                    StringComparison.OrdinalIgnoreCase))
            {
                return product;
            }
        }

        return string.Empty;
    }

    private static string ReadFieldValue(
        JsonElement application,
        params string[] names)
    {
        if (!application.TryGetProperty(
                "fields",
                out var fields) ||
            fields.ValueKind !=
                JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var field in
                 fields.EnumerateArray())
        {
            var name =
                ReadString(
                    field,
                    "name");

            if (!names.Contains(
                    name,
                    StringComparer.OrdinalIgnoreCase) ||
                !field.TryGetProperty(
                    "value",
                    out var value))
            {
                continue;
            }

            return value.ValueKind ==
                JsonValueKind.String
                    ? value.GetString() ??
                      string.Empty
                    : value.ToString();
        }

        return string.Empty;
    }

    private static string ReadString(
        JsonElement root,
        string property)
    {
        if (!root.TryGetProperty(
                property,
                out var value))
        {
            return string.Empty;
        }

        return value.ValueKind ==
            JsonValueKind.String
                ? value.GetString() ??
                  string.Empty
                : value.ToString();
    }

    private static bool SafeContainerName(
        string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        System.Text.RegularExpressions.Regex.IsMatch(
            value,
            "^[A-Za-z0-9][A-Za-z0-9_.-]*$");

    private static async Task<(
        bool Success,
        string Output)>
        RunProcessAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            bool acceptNonZeroWithOutput = false)
    {
        try
        {
            using var process =
                new Process
                {
                    StartInfo =
                        new ProcessStartInfo
                        {
                            FileName =
                                fileName,
                            RedirectStandardOutput =
                                true,
                            RedirectStandardError =
                                true,
                            UseShellExecute =
                                false,
                            CreateNoWindow =
                                true
                        }
                };

            foreach (var argument in arguments)
            {
                process.StartInfo
                    .ArgumentList.Add(argument);
            }

            if (!process.Start())
                return (false, string.Empty);

            var outputTask =
                process.StandardOutput
                    .ReadToEndAsync(
                        cancellationToken);
            var errorTask =
                process.StandardError
                    .ReadToEndAsync(
                        cancellationToken);

            using var linked =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);
            linked.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(
                    linked.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(
                        entireProcessTree: true);
                }

                return (
                    false,
                    string.Empty);
            }

            var output =
                await outputTask;
            _ = await errorTask;

            var success =
                process.ExitCode == 0 ||
                (
                    acceptNonZeroWithOutput &&
                    !string.IsNullOrWhiteSpace(
                        output)
                );

            return (
                success,
                output);
        }
        catch
        {
            return (
                false,
                string.Empty);
        }
    }

    private static string CandidateKey(
        string hostScope,
        string product,
        string endpoint,
        string parent) =>
        HashKey(
            "candidate",
            hostScope,
            product,
            CanonicalUrl(endpoint),
            parent);

    private static string StableVerifiedKey(
        string hostScope,
        string parent,
        ArrStatusFingerprint fingerprint,
        string launchUrl)
    {
        return HashKey(
            "arr",
            hostScope,
            parent,
            fingerprint.Product,
            fingerprint.AppData,
            fingerprint.StartupPath,
            fingerprint.InstanceName,
            CanonicalHostAndPath(
                launchUrl));
    }

    private static string HashKey(
        string prefix,
        params string?[] values)
    {
        var input =
            string.Join(
                "\u001f",
                values.Select(value =>
                    value?.Trim()
                        .ToLowerInvariant() ??
                    string.Empty));
        var hash =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(input)))
                .ToLowerInvariant();

        return
            $"{prefix}|{hash[..24]}";
    }

    private static string SecretFingerprint(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "none";

        var hash =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(value)))
                .ToLowerInvariant();

        return hash[..12];
    }

    private static string FirstMeaningful(
        params string[] values) =>
        values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value)) ??
        "unknown-instance";

    private static string CanonicalHostAndPath(
        string value)
    {
        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri))
        {
            return value;
        }

        return
            $"{uri.DnsSafeHost.ToLowerInvariant()}" +
            $"{uri.AbsolutePath.TrimEnd('/').ToLowerInvariant()}";
    }

    private static bool EquivalentBase(
        string left,
        string right) =>
        CanonicalUrl(left).Equals(
            CanonicalUrl(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool SamePort(
        string left,
        string right) =>
        Uri.TryCreate(
            left,
            UriKind.Absolute,
            out var leftUri) &&
        Uri.TryCreate(
            right,
            UriKind.Absolute,
            out var rightUri) &&
        leftUri.Port ==
            rightUri.Port &&
        leftUri.DnsSafeHost.Equals(
            rightUri.DnsSafeHost,
            StringComparison.OrdinalIgnoreCase);

    private static string CanonicalUrl(
        string value)
    {
        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri))
        {
            return value?.Trim() ??
                   string.Empty;
        }

        var builder =
            new UriBuilder(uri)
            {
                Query = string.Empty,
                Fragment = string.Empty,
                Path =
                    uri.AbsolutePath.TrimEnd('/')
            };

        return builder.Uri
            .ToString()
            .TrimEnd('/');
    }

    private static string PreserveVerifiedDetail(
        string existing,
        string state,
        string detail)
    {
        var secondary =
            $"Additional probe returned {state}";

        if (!string.IsNullOrWhiteSpace(detail))
        {
            secondary +=
                $": {detail}";
        }

        secondary +=
            ". The strongest verified result from this capture was retained.";

        if (existing.Contains(
                secondary,
                StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return string.IsNullOrWhiteSpace(existing)
            ? secondary
            : existing + " " + secondary;
    }

    private static string AppendEvidence(
        string existing,
        string addition)
    {
        if (existing.Contains(
                addition,
                StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return string.IsNullOrWhiteSpace(existing)
            ? addition
            : existing + " · " + addition;
    }
}
