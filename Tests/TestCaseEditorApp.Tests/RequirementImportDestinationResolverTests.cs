using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests
{
    [TestClass]
    public class RequirementImportDestinationResolverTests
    {
        [TestMethod]
        public void ResolvePreferredParentContainerId_PrefersExplicitSelectionOverFallbacks()
        {
            var result = RequirementImportDestinationResolver.ResolvePreferredParentContainerId(
                explicitlySelectedContainerId: 9876,
                attachmentContainerId: 1234,
                environmentOverrideContainerId: "5555");

            Assert.AreEqual(9876, result);
        }

        [TestMethod]
        public void ResolvePreferredParentContainerId_UsesEnvironmentOverrideWhenNoExplicitSelection()
        {
            var result = RequirementImportDestinationResolver.ResolvePreferredParentContainerId(
                explicitlySelectedContainerId: null,
                attachmentContainerId: 1234,
                environmentOverrideContainerId: "5555");

            Assert.AreEqual(5555, result);
        }

        [TestMethod]
        public void ResolvePreferredParentContainerId_FallsBackToAttachmentContainerWhenNoOtherChoiceExists()
        {
            var result = RequirementImportDestinationResolver.ResolvePreferredParentContainerId(
                explicitlySelectedContainerId: null,
                attachmentContainerId: 1234,
                environmentOverrideContainerId: null);

            Assert.AreEqual(1234, result);
        }
    }
}
