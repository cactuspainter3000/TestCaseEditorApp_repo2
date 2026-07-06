using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace TestCaseEditorApp.Tests.Regression
{
    [TestClass]
    public class StartupDashboardFlowRegressionTests
    {
        [TestMethod]
        public void OpenRecentWorkshop_UsesDashboardOpenFlow_AndNavigatesToRequirements()
        {
            var source = ReadStartupMainViewModelSource();

            Assert.IsTrue(source.Contains("private async Task OpenRecentWorkshopAsync(string? filePath)", StringComparison.Ordinal),
                "Expected OpenRecentWorkshopAsync command entry-point in StartUp_MainViewModel.");

            Assert.IsTrue(source.Contains("await OpenWorkshopFileAsync(filePath);", StringComparison.Ordinal),
                "Expected OpenRecentWorkshopAsync to route through OpenWorkshopFileAsync for a single dashboard-first open path.");

            Assert.IsTrue(source.Contains("await _openProjectMediator.OpenProjectFileAsync(filePath);", StringComparison.Ordinal),
                "Regression detected: dashboard open flow no longer uses OpenProject mediator.");

            Assert.IsTrue(source.Contains("_navigationMediator.NavigateToSection(\"requirements\");", StringComparison.Ordinal),
                "Regression detected: successful dashboard open no longer navigates to Requirements.");
        }

        [TestMethod]
        public void BrowseWorkshop_RemainsDashboardBrowseEntryPoint()
        {
            var source = ReadStartupMainViewModelSource();

            Assert.IsTrue(source.Contains("private async Task BrowseWorkshopAsync()", StringComparison.Ordinal),
                "Expected BrowseWorkshopAsync command in StartUp_MainViewModel.");

            Assert.IsTrue(source.Contains("new OpenFileDialog", StringComparison.Ordinal),
                "Expected BrowseWorkshopAsync to use OpenFileDialog for manual workshop selection.");

            Assert.IsTrue(source.Contains("Title = \"Open a Requirements Workshop\"", StringComparison.Ordinal),
                "Expected Browse dialog title to remain aligned with workshop terminology.");

            Assert.IsTrue(source.Contains("await BrowseWorkshopAsync();", StringComparison.Ordinal),
                "Expected ViewAllWorkshopsAsync to delegate to BrowseWorkshopAsync (legacy OpenProject screen retired).");
        }

        [TestMethod]
        public void RefreshDashboardData_MarshalsCollectionUpdatesToDispatcher()
        {
            var source = ReadStartupMainViewModelSource();

            Assert.IsTrue(source.Contains("Application.Current?.Dispatcher?.CheckAccess() == true", StringComparison.Ordinal),
                "Expected UI-thread guard in RefreshDashboardData.");

            Assert.IsTrue(source.Contains("dispatcher.InvokeAsync(RefreshDashboardDataCore)", StringComparison.Ordinal),
                "Expected background-thread updates to marshal via Dispatcher.InvokeAsync.");

            Assert.IsTrue(source.Contains("private void RefreshDashboardDataCore()", StringComparison.Ordinal),
                "Expected refresh core method for dispatcher-safe collection/property updates.");
        }

        private static string ReadStartupMainViewModelSource()
        {
            var filePath = ResolveRepoFilePath("MVVM", "Domains", "Startup", "ViewModels", "StartUp_MainViewModel.cs");
            return File.ReadAllText(filePath);
        }

        private static string ResolveRepoFilePath(params string[] relativeParts)
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                var projectFile = Path.Combine(current.FullName, "TestCaseEditorApp.csproj");
                if (File.Exists(projectFile))
                {
                    return Path.Combine(current.FullName, Path.Combine(relativeParts));
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root from test base directory.");
        }
    }
}
