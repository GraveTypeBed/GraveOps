namespace GraveOps.App.Models;

public sealed class PlexSessionRow
{
    public string User { get; set; } = "";
    public string Title { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string Player { get; set; } = "";
    public string Product { get; set; } = "";
    public string Platform { get; set; } = "";
    public string State { get; set; } = "";
    public string Location { get; set; } = "";
    public string Address { get; set; } = "";
    public string Decision { get; set; } = "DIRECT PLAY";
    public string ProgressText { get; set; } = "";
    public double ProgressPercent { get; set; }
    public long BandwidthKbps { get; set; }
    public string BandwidthText { get; set; } = "--";
    public string Quality { get; set; } = "--";
    public string VideoCodec { get; set; } = "--";
    public string AudioCodec { get; set; } = "--";
    public string Container { get; set; } = "--";
    public string VideoDecision { get; set; } = "";
    public string AudioDecision { get; set; } = "";
    public string ContainerDecision { get; set; } = "";
    public string TranscodeSpeed { get; set; } = "";
    public string TranscodeProgress { get; set; } = "";
    public string HardwareTranscode { get; set; } = "";
    public string SecureText { get; set; } = "";
    public string RelayedText { get; set; } = "";
    public string SessionId { get; set; } = "";

    public string DetailText =>
        $"User: {User}\n" +
        $"Media: {Title}\n" +
        $"Type: {MediaType}\n" +
        $"Decision: {Decision}\n" +
        $"Progress: {ProgressText}\n" +
        $"Bandwidth: {BandwidthText}\n" +
        $"Quality: {Quality}\n" +
        $"Video: {VideoCodec} [{VideoDecision}]\n" +
        $"Audio: {AudioCodec} [{AudioDecision}]\n" +
        $"Container: {Container} [{ContainerDecision}]\n" +
        $"Player: {Player}\n" +
        $"Product: {Product}\n" +
        $"Platform: {Platform}\n" +
        $"Playback state: {State}\n" +
        $"Location: {Location}\n" +
        $"Address: {Address}\n" +
        $"Secure: {SecureText}\n" +
        $"Relayed: {RelayedText}\n" +
        $"Transcode speed: {TranscodeSpeed}\n" +
        $"Transcode progress: {TranscodeProgress}\n" +
        $"Hardware transcode: {HardwareTranscode}\n" +
        $"Session ID: {SessionId}";
}

public sealed class PlexSessionSnapshot
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string ServerName { get; set; } = "";
    public string Version { get; set; } = "--";
    public string MachineIdentifier { get; set; } = "";
    public string Platform { get; set; } = "";
    public List<PlexSessionRow> Sessions { get; set; } = new();

    public int SessionCount => Sessions.Count;
    public int DirectPlayCount =>
        Sessions.Count(x => x.Decision == "DIRECT PLAY");
    public int DirectStreamCount =>
        Sessions.Count(x => x.Decision == "DIRECT STREAM");
    public int TranscodeCount =>
        Sessions.Count(x => x.Decision == "TRANSCODE");
    public long TotalBandwidthKbps =>
        Sessions.Sum(x => x.BandwidthKbps);

    public string TotalBandwidthText =>
        FormatKbps(TotalBandwidthKbps);

    public static string FormatKbps(long kbps)
    {
        if (kbps <= 0)
            return "0 Kbps";

        if (kbps >= 1000)
            return $"{kbps / 1000d:0.0} Mbps";

        return $"{kbps} Kbps";
    }
}