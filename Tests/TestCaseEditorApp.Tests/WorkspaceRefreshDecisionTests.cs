using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests
{
    [TestClass]
    public class WorkspaceRefreshDecisionTests
    {
        [TestMethod]
        public void ShouldPromptForJamaRefresh_WhenWorkspaceWasSavedAfterLastSync_ReturnsTrue()
        {
            var workspace = new Workspace
            {
                LastSavedUtc = DateTime.UtcNow,
                LastJamaSyncUtc = DateTime.UtcNow.AddMinutes(-10)
            };

            var shouldPrompt = WorkspaceService.ShouldPromptForRefresh(workspace, workspace.LastJamaSyncUtc);

            Assert.IsTrue(shouldPrompt);
        }

        [TestMethod]
        public void ShouldPromptForJamaRefresh_WhenNoPriorSyncExists_ReturnsFalse()
        {
            var workspace = new Workspace
            {
                LastSavedUtc = DateTime.UtcNow,
                LastJamaSyncUtc = null
            };

            var shouldPrompt = WorkspaceService.ShouldPromptForRefresh(workspace, workspace.LastJamaSyncUtc);

            Assert.IsFalse(shouldPrompt);
        }

        [TestMethod]
        public void ShouldPromptForRagRefresh_WhenWorkspaceWasSavedAfterLastSync_ReturnsTrue()
        {
            var workspace = new Workspace
            {
                LastSavedUtc = DateTime.UtcNow,
                LastRagSyncUtc = DateTime.UtcNow.AddMinutes(-10)
            };

            var shouldPrompt = WorkspaceService.ShouldPromptForRefresh(workspace, workspace.LastRagSyncUtc);

            Assert.IsTrue(shouldPrompt);
        }
    }
}
