using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Examples
{
    /// <summary>
    /// Example usage of PowerPoint RAG functionality for querying presentation content.
    /// This class demonstrates how to upload PowerPoints and ask questions about their content.
    /// </summary>
    public static class PowerPointRagExample
    {
        /// <summary>
        /// Example: Upload PowerPoints and query their content
        /// </summary>
        public static async Task<string> ExampleUsageAsync()
        {
            try
            {
                // Get the PowerPoint RAG service from DI container
                var powerPointRag = App.ServiceProvider?.GetService<IPowerPointRagService>();
                if (powerPointRag?.IsConfigured != true)
                {
                    return "❌ PowerPoint RAG service not available or not properly configured. Please ensure Ollama is running and DirectRAG is enabled.";
                }

                var workspaceId = "my-project"; // Group related presentations
                var results = new List<string>();

                // Example 1: Index a PowerPoint presentation
                results.Add("📁 **Indexing PowerPoint Presentations:**");
                var indexed = await powerPointRag.IndexPowerPointAsync(
                    filePath: @"C:\path\to\your\presentation.pptx", 
                    documentName: "Project Overview", 
                    workspaceId: workspaceId
                );
                results.Add(indexed ? "✅ Successfully indexed Project Overview presentation" 
                                   : "❌ Failed to index presentation");

                // Example 2: Query the presentations
                results.Add("\n🔍 **Querying Presentation Content:**");
                var answer = await powerPointRag.GenerateAnswerAsync(
                    question: "Give me a 3-sentence narrative describing this project", 
                    workspaceId: workspaceId
                );
                results.Add($"💬 **Answer:** {answer}");

                // Example 3: Search for specific content
                results.Add("\n🎯 **Searching for Specific Content:**");
                var searchResults = await powerPointRag.QueryPresentationsAsync(
                    query: "project timeline milestones", 
                    workspaceId: workspaceId, 
                    maxResults: 3
                );
                
                foreach (var result in searchResults)
                {
                    results.Add($"📄 **{result.DocumentName}** (Slide {result.SlideNumber}) - Score: {result.SimilarityScore:F2}");
                    results.Add($"   {result.ContentPreview}");
                }

                // Example 4: List indexed presentations
                results.Add("\n📋 **Indexed Presentations:**");
                var presentations = await powerPointRag.GetIndexedPresentationsAsync(workspaceId);
                foreach (var pres in presentations)
                {
                    results.Add($"📊 {pres.DocumentName} ({pres.SlideCount} slides, {pres.FileSizeDisplay})");
                }

                return string.Join("\n", results);
            }
            catch (Exception ex)
            {
                return $"❌ Error during PowerPoint RAG example: {ex.Message}";
            }
        }

        /// <summary>
        /// Simple helper to demonstrate asking questions about your PowerPoints
        /// </summary>
        /// <param name="powerPointPath">Path to your PowerPoint file</param>
        /// <param name="question">Your question about the presentation</param>
        /// <returns>AI-generated answer based on slide content</returns>
        public static async Task<string> AskAboutPowerPointAsync(string powerPointPath, string question)
        {
            try
            {
                var powerPointRag = App.ServiceProvider?.GetService<IPowerPointRagService>();
                if (powerPointRag?.IsConfigured != true)
                {
                    return "PowerPoint RAG service not available. Please ensure Ollama is running.";
                }

                // Use the filename as workspace ID for simplicity
                var workspaceId = System.IO.Path.GetFileNameWithoutExtension(powerPointPath);
                
                // Index the presentation (this is safe to call multiple times)
                var indexed = await powerPointRag.IndexPowerPointAsync(powerPointPath, workspaceId: workspaceId);
                if (!indexed)
                {
                    return $"Failed to index PowerPoint: {powerPointPath}";
                }

                // Get the answer
                var answer = await powerPointRag.GenerateAnswerAsync(question, workspaceId);
                return answer;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Batch index multiple PowerPoint files into a workspace
        /// </summary>
        /// <param name="powerPointPaths">List of PowerPoint file paths</param>
        /// <param name="workspaceId">Workspace to group them under</param>
        /// <returns>Number of successfully indexed files</returns>
        public static async Task<int> IndexMultiplePowerPointsAsync(IEnumerable<string> powerPointPaths, string workspaceId = "default")
        {
            var powerPointRag = App.ServiceProvider?.GetService<IPowerPointRagService>();
            if (powerPointRag?.IsConfigured != true) return 0;

            int successCount = 0;
            foreach (var path in powerPointPaths)
            {
                try
                {
                    var documentName = System.IO.Path.GetFileNameWithoutExtension(path);
                    var indexed = await powerPointRag.IndexPowerPointAsync(path, documentName, workspaceId);
                    if (indexed) successCount++;
                }
                catch
                {
                    // Continue with other files if one fails
                    continue;
                }
            }
            return successCount;
        }

        /// <summary>
        /// Example questions you can ask about your PowerPoints
        /// </summary>
        public static readonly string[] ExampleQuestions = 
        {
            "Give me a 3-sentence narrative describing this project",
            "What are the main goals and objectives?", 
            "What is the project timeline and key milestones?",
            "Who are the key stakeholders mentioned?",
            "What are the main challenges or risks discussed?",
            "What is the budget or financial information?",
            "What technologies or tools are mentioned?",
            "What are the expected deliverables?",
            "Summarize the technical approach",
            "What are the success criteria or metrics?"
        };
    }
}

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Extension methods to make PowerPoint RAG easier to use
    /// </summary>
    public static class PowerPointRagExtensions
    {
        /// <summary>
        /// Quick method to ask a question about a PowerPoint file
        /// </summary>
        public static async Task<string> AskQuestionAsync(this IPowerPointRagService service, 
            string powerPointPath, string question, CancellationToken cancellationToken = default)
        {
            if (!service.IsConfigured) return "Service not configured";
            
            var workspaceId = System.IO.Path.GetFileNameWithoutExtension(powerPointPath);
            var documentName = System.IO.Path.GetFileNameWithoutExtension(powerPointPath);
            
            // Index the file
            await service.IndexPowerPointAsync(powerPointPath, documentName, workspaceId, cancellationToken);
            
            // Get answer
            return await service.GenerateAnswerAsync(question, workspaceId, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Get all content from a PowerPoint as searchable chunks
        /// </summary>
        public static async Task<List<PowerPointSearchResult>> GetAllContentAsync(this IPowerPointRagService service,
            string powerPointPath, CancellationToken cancellationToken = default)
        {
            if (!service.IsConfigured) return new List<PowerPointSearchResult>();
            
            var workspaceId = System.IO.Path.GetFileNameWithoutExtension(powerPointPath);
            var documentName = System.IO.Path.GetFileNameWithoutExtension(powerPointPath);
            
            // Index the file
            await service.IndexPowerPointAsync(powerPointPath, documentName, workspaceId, cancellationToken);
            
            // Search with very broad query to get all content
            return await service.QueryPresentationsAsync("presentation content slides text", workspaceId, 
                maxResults: 50, similarityThreshold: 0.1f, cancellationToken);
        }
    }
}