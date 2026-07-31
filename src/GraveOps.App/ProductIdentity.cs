namespace GraveOps.App;

public static class ProductIdentity
{
    public const string ProductName = "GraveOps";
    public const string DisplayName = "GraveOps";

    // Keep the established legacy storage/credential namespaces so upgrading to 2.0
    // does not orphan existing host profiles or secrets. These names are internal only.
    public const string DataDirectoryName = "GraveOps Community";
    public const string CredentialNamespace = "GraveOpsCommunity";
}
