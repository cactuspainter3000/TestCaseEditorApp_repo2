namespace TestCaseEditorApp.Services
{
    public static class RequirementImportDestinationResolver
    {
        public static int? ResolvePreferredParentContainerId(int? explicitlySelectedContainerId, int? attachmentContainerId, string? environmentOverrideContainerId)
        {
            if (explicitlySelectedContainerId.HasValue && explicitlySelectedContainerId.Value > 0)
            {
                return explicitlySelectedContainerId.Value;
            }

            if (!string.IsNullOrWhiteSpace(environmentOverrideContainerId) && int.TryParse(environmentOverrideContainerId, out var envContainerId) && envContainerId > 0)
            {
                return envContainerId;
            }

            return attachmentContainerId.HasValue && attachmentContainerId.Value > 0
                ? attachmentContainerId.Value
                : null;
        }
    }
}
