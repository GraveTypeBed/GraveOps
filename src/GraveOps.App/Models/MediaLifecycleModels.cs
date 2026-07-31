namespace GraveOps.App.Models;

public enum LifecycleStage
{
    Request = 0,
    Discovery = 1,
    Arr = 2,
    Download = 3,
    Import = 4,
    Processing = 5,
    Library = 6
}

public sealed class MediaLifecycleItem
{
    public string Title { get; set; } = "";
    public string MediaType { get; set; } = "Media";
    public string OwnerApp { get; set; } = "";
    public LifecycleStage Stage { get; set; } = LifecycleStage.Arr;
    public string StageText => Stage.ToString().ToUpperInvariant();
    public string State { get; set; } = "";
    public string Progress { get; set; } = "--";
    public string Remaining { get; set; } = "--";
    public string Detail { get; set; } = "";
    public bool NeedsAttention { get; set; }
    public string DeepLink { get; set; } = "page:Applications";
    public string PathText { get; set; } = "Request → Discovery → Arr → Download → Import → Processing → Library";
}

public sealed class MediaLifecycleSnapshot
{
    public DateTimeOffset SampledAt { get; set; } = DateTimeOffset.Now;
    public Guid? ServerId { get; set; }
    public string ServerName { get; set; } = "";
    public List<MediaLifecycleItem> Items { get; set; } = new();
    public bool HasSeerr { get; set; }
    public bool HasBazarr { get; set; }
    public bool HasTdarr { get; set; }
    public bool HasLibrary { get; set; }

    public int ActiveCount => Items.Count;
    public int AttentionCount => Items.Count(x => x.NeedsAttention);
    public int DownloadingCount => Items.Count(x => x.Stage == LifecycleStage.Download);
    public int ImportCount => Items.Count(x => x.Stage == LifecycleStage.Import);
}

public sealed class RemediationStep
{
    public int Order { get; set; }
    public string Severity { get; set; } = "INFO";
    public string Component { get; set; } = "";
    public string Title { get; set; } = "";
    public string Why { get; set; } = "";
    public string NextAction { get; set; } = "";
    public string DeepLink { get; set; } = "page:Dashboard";
}
