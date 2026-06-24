using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace TestCaseEditorApp.Tests.Regression
{
    [TestClass]
    public class NewProjectMediatorRegressionTests
    {
        [TestMethod]
        public void CompleteProjectCreationAsync_UsesBackgroundTask_ForRagSetupCalls()
        {
            var mediatorPath = ResolveRepoFilePath("MVVM", "Domains", "NewProject", "Mediators", "NewProjectMediator.cs");
            var source = File.ReadAllText(mediatorPath);

            const string methodSignature = "public async Task<bool> CompleteProjectCreationAsync";
            var methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
            Assert.IsTrue(methodStart >= 0, "Could not find CompleteProjectCreationAsync method.");

            var methodBody = ExtractMethodBody(source, methodStart);

            var taskRunIndex = methodBody.IndexOf("Task.Run(async () =>", StringComparison.Ordinal);
            Assert.IsTrue(taskRunIndex >= 0,
                "Expected CompleteProjectCreationAsync to defer RAG setup using Task.Run(async () => ...). This prevents UI hangs during project creation.");

            var taskRunOpenBrace = methodBody.IndexOf('{', taskRunIndex);
            Assert.IsTrue(taskRunOpenBrace >= 0, "Could not locate Task.Run block opening brace.");

            var taskRunCloseBrace = FindMatchingBrace(methodBody, taskRunOpenBrace);
            Assert.IsTrue(taskRunCloseBrace > taskRunOpenBrace, "Could not locate Task.Run block closing brace.");

            AssertCallIsInsideBackgroundBlock(methodBody, "await _anythingLLMService.UploadOptimizationGuideAsync", taskRunIndex, taskRunCloseBrace);
            AssertCallIsInsideBackgroundBlock(methodBody, "await _anythingLLMService.UploadRagTrainingDocumentsAsync", taskRunIndex, taskRunCloseBrace);
            AssertCallIsInsideBackgroundBlock(methodBody, "await VerifyStandardRagDocumentsEmbeddedAsync", taskRunIndex, taskRunCloseBrace);
        }

        private static void AssertCallIsInsideBackgroundBlock(string methodBody, string awaitedCall, int taskRunStartIndex, int taskRunEndIndex)
        {
            var callIndex = methodBody.IndexOf(awaitedCall, StringComparison.Ordinal);
            Assert.IsTrue(callIndex >= 0, $"Expected to find '{awaitedCall}' in CompleteProjectCreationAsync.");

            Assert.IsTrue(callIndex > taskRunStartIndex && callIndex < taskRunEndIndex,
                $"Regression detected: '{awaitedCall}' is no longer in the Task.Run background block and may block project creation.");
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
