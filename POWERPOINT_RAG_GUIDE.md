# PowerPoint RAG Integration Guide

## Overview

Your TestCaseEditorApp now supports **PowerPoint RAG (Retrieval-Augmented Generation)**! This allows you to:

- 📊 **Upload PowerPoint presentations** and have them automatically indexed
- 🔍 **Ask questions** about presentation content using natural language 
- 💬 **Get AI-generated answers** based only on the slide content
- 🎯 **Search for specific information** across multiple presentations
- 📋 **Organize presentations** into workspaces for different projects

## Prerequisites

✅ **Ollama must be running** (you confirmed this is working)  
✅ **DirectRAG service enabled** (already configured in your app)  
✅ **LLM service available** (already integrated)

## Quick Start

### Basic Usage (Simplest Approach)

```csharp
// Get the service from your DI container
var powerPointRag = App.ServiceProvider?.GetService<IPowerPointRagService>();

// Ask a question about a PowerPoint file
string answer = await powerPointRag.AskQuestionAsync(
    @"C:\path\to\your\presentation.pptx",
    "Give me a 3-sentence narrative describing this project"
);

Console.WriteLine(answer);
```

### Advanced Usage

```csharp
// 1. Index multiple presentations in a workspace
var workspaceId = "my-project";
await powerPointRag.IndexPowerPointAsync(@"C:\project\overview.pptx", "Overview", workspaceId);
await powerPointRag.IndexPowerPointAsync(@"C:\project\timeline.pptx", "Timeline", workspaceId);
await powerPointRag.IndexPowerPointAsync(@"C:\project\budget.pptx", "Budget", workspaceId);

// 2. Ask questions that span all presentations
var answer = await powerPointRag.GenerateAnswerAsync(
    "What is the project timeline and budget?", 
    workspaceId
);

// 3. Search for specific content
var results = await powerPointRag.QueryPresentationsAsync(
    "technical requirements", 
    workspaceId, 
    maxResults: 5
);

foreach (var result in results)
{
    Console.WriteLine($"{result.DocumentName} (Slide {result.SlideNumber}): {result.ContentPreview}");
}
```

## Example Questions You Can Ask

- **Project Overview:** "Give me a 3-sentence narrative describing this project"
- **Timeline:** "What are the key milestones and deadlines?"
- **Budget:** "What is the total budget and major cost categories?"
- **Technical:** "What technologies and tools are mentioned?"
- **Stakeholders:** "Who are the key people and organizations involved?"
- **Risks:** "What challenges or risks are identified?"

## Supported PowerPoint Features

### ✅ What's Extracted:
- 📝 **Text content** from slides (titles, bullet points, paragraphs)
- 📊 **Table content** (text within table cells)
- 🎯 **Text boxes** and shapes with text
- 📑 **All slide content** maintaining slide boundaries

### ❌ What's Not Extracted:
- 🖼️ **Images** (visual content)
- 📈 **Charts/graphs** (visual data representations)
- 🎨 **Design elements** (colors, layouts, animations)
- 🔗 **Hyperlinks** (link destinations)

## Workspace Organization

**Workspaces** help you organize related presentations:

```csharp
// Different projects in separate workspaces
await powerPointRag.IndexPowerPointAsync(file1, "Doc1", workspaceId: "project-alpha");
await powerPointRag.IndexPowerPointAsync(file2, "Doc2", workspaceId: "project-beta");

// Query specific project
var alphaAnswer = await powerPointRag.GenerateAnswerAsync(question, "project-alpha");
var betaAnswer = await powerPointRag.GenerateAnswerAsync(question, "project-beta");

// List what's in each workspace
var alphaFiles = await powerPointRag.GetIndexedPresentationsAsync("project-alpha");
```

## API Reference

### Core Methods

| Method | Description |
|--------|-------------|
| `IndexPowerPointAsync()` | Upload and index a PowerPoint file |
| `GenerateAnswerAsync()` | Ask questions and get AI answers |
| `QueryPresentationsAsync()` | Search for specific content chunks |
| `GetIndexedPresentationsAsync()` | List uploaded presentations |
| `ClearWorkspaceAsync()` | Remove all presentations from workspace |

### Helper Extensions

| Method | Description |
|--------|-------------|
| `AskQuestionAsync()` | Simple one-step question asking |
| `GetAllContentAsync()` | Extract all content as searchable chunks |

## Performance & Storage

- **Text Extraction:** ~1-2 seconds per presentation (depends on slide count)
- **Indexing:** Creates embeddings locally using Ollama
- **Storage:** Indexes stored in `%AppData%\TestCaseEditorApp\DocumentIndexes\`
- **Memory:** Efficient chunking prevents memory issues with large presentations

## Error Handling

The service handles common issues gracefully:

```csharp
if (!powerPointRag.IsConfigured)
{
    // Ollama not running or DirectRAG not available
    Console.WriteLine("PowerPoint RAG not available");
}

var indexed = await powerPointRag.IndexPowerPointAsync(filePath);
if (!indexed)
{
    // File not found, corrupted, or extraction failed
    Console.WriteLine("Failed to index PowerPoint");
}
```

## Integration with Existing Features

This PowerPoint RAG service integrates seamlessly with your existing infrastructure:

- ✅ **Uses your existing Ollama setup** (same as other LLM features)
- ✅ **Leverages DirectRAG service** (same embedding engine)
- ✅ **Follows architectural patterns** (dependency injection, logging, etc.)
- ✅ **Works alongside Jama integration** (can enhance requirements with presentation context)

## Troubleshooting

### "PowerPoint RAG service not available"
- Ensure Ollama is running: `ollama --version`
- Check that DirectRAG service is registered in DI
- Verify LLM services are properly configured

### "Failed to index PowerPoint"
- Check file path exists and is readable
- Ensure file is valid PowerPoint format (.pptx, .ppt)
- Verify sufficient disk space for embeddings storage

### Poor answer quality
- Try more specific questions
- Ensure presentations contain relevant text content
- Consider indexing multiple related presentations in same workspace
- Adjust similarity threshold in search queries

## Next Steps

1. **Try the examples** in `Examples/PowerPointRagExample.cs`
2. **Index your project presentations** using the workspace concept
3. **Experiment with different question styles** to see what works best
4. **Consider integrating** with your existing requirement workflows

The PowerPoint RAG functionality is now ready to help you extract insights from your presentation library!