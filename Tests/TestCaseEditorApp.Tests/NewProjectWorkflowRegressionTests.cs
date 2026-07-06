using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace TestCaseEditorApp.Tests.Regression
{
    [TestClass]
    public class NewProjectWorkflowRegressionTests
    {
        [TestMethod]
        public void CreateProject_PublishesOnlyCanonicalProjectCreatedEvent()
        {
            var vmPath = ResolveRepoFilePath("MVVM", "Domains", "NewProject", "ViewModels", "NewProjectWorkflowViewModel.cs");
            var source = File.ReadAllText(vmPath);

            const string methodSignature = "private async void CreateProject()";
            var methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
            Assert.IsTrue(methodStart >= 0, "Could not find CreateProject method.");

            var methodBody = ExtractMethodBody(source, methodStart);

            Assert.IsTrue(
                methodBody.Contains("new NewProjectEvents.ProjectCreatedWithWorkspace", StringComparison.Ordinal),
                "Expected canonical ProjectCreatedWithWorkspace event publication.");

            Assert.IsFalse(
                methodBody.Contains("JamaProjectCreated", StringComparison.Ordinal),
                "Legacy JamaProjectCreated publication should not be present.");

            Assert.IsFalse(
                methodBody.Contains("ProjectCreated?.Invoke", StringComparison.Ordinal),
                "Legacy ProjectCreated event invocation should not be present.");
        }

        [TestMethod]
        public void CreateProject_UsesProjectCompletedEventOnlyForViewModelCompletionSignal()
        {
            var vmPath = ResolveRepoFilePath("MVVM", "Domains", "NewProject", "ViewModels", "NewProjectWorkflowViewModel.cs");
            var source = File.ReadAllText(vmPath);

            const string methodSignature = "private async void CreateProject()";
            var methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
            Assert.IsTrue(methodStart >= 0, "Could not find CreateProject method.");

            var methodBody = ExtractMethodBody(source, methodStart);

            Assert.IsTrue(
                methodBody.Contains("ProjectCompleted?.Invoke", StringComparison.Ordinal),
                "ProjectCompleted signal should remain for workflow completion handling.");
        }

        private static string ExtractMethodBody(string source, int methodStart)
        {
            var openingBrace = source.IndexOf('{', methodStart);
            Assert.IsTrue(openingBrace >= 0, "Could not locate method opening brace.");

            var closingBrace = FindMatchingBrace(source, openingBrace);
            Assert.IsTrue(closingBrace > openingBrace, "Could not locate method closing brace.");

            return source.Substring(openingBrace, closingBrace - openingBrace + 1);
        }

        private static int FindMatchingBrace(string text, int openingBraceIndex)
        {
            var depth = 0;
            for (var i = openingBraceIndex; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    depth++;
                }
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
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
