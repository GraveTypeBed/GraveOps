namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private readonly GraveOpsDesktopArchitecture _desktopArchitecture =
        GraveOpsDesktopArchitecture.CreateDefault();

    private IPlatformHardeningPort PlatformHardening =>
        _desktopArchitecture.PlatformHardening;
    private IProductOperationsPort ProductOperations =>
        _desktopArchitecture.Products;
    private IHealthPolicyPort HealthPolicy =>
        _desktopArchitecture.Health;
    private ISignalQualityStorePort SignalQualityStore =>
        _desktopArchitecture.SignalQualityStore;
    private IRemediationPolicyPort RemediationPolicy =>
        _desktopArchitecture.Remediation;
    private IRemediationStorePort RemediationStore =>
        _desktopArchitecture.RemediationStore;
    private IUiProjectionPort UiProjection =>
        _desktopArchitecture.UiProjection;
}
