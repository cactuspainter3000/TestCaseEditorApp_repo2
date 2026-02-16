using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for converting requirements and test cases to Jama format
    /// </summary>
    public class JamaTestCaseConversionService : IJamaTestCaseConversionService
    {
        private readonly ILogger<JamaTestCaseConversionService> _logger;

        public JamaTestCaseConversionService(ILogger<JamaTestCaseConversionService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Convert SINGLE requirement's first test case to Jama format (for debugging)
        /// </summary>
        public List<JamaTestCaseRequest> ConvertSingleTestCaseToJamaFormat(Requirement requirement)
        {
            var jamaTestCases = new List<JamaTestCaseRequest>();

            try
            {
                // Priority: Handle saved test cases first (more reliable)
                if (requirement.GeneratedTestCases?.Any() == true)
                {
                    var firstSavedTestCase = requirement.GeneratedTestCases.First();
                    var jamaTestCase = ConvertSavedTestCase(firstSavedTestCase, requirement);
                    if (jamaTestCase != null)
                    {
                        jamaTestCases.Add(jamaTestCase);
                        _logger?.LogInformation("Converted first saved test case from requirement {RequirementId}: {TestCaseName}",
                            requirement.GlobalId ?? requirement.Item ?? "Unknown", firstSavedTestCase.Name ?? "Unnamed");
                    }
                }
                // Fallback: Handle AI-generated test case if no saved test cases
                else if (!string.IsNullOrWhiteSpace(requirement.CurrentResponse?.Output))
                {
                    var aiTestCase = ConvertAIGeneratedTestCase(requirement);
                    if (aiTestCase != null)
                    {
                        jamaTestCases.Add(aiTestCase);
                        _logger?.LogInformation("Converted AI-generated test case from requirement {RequirementId}",
                            requirement.GlobalId ?? requirement.Item ?? "Unknown");
                    }
                }

                if (!jamaTestCases.Any())
                {
                    _logger?.LogWarning("No convertible test cases found for requirement {RequirementId}",
                        requirement.GlobalId ?? requirement.Item ?? "Unknown");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error converting single test case for requirement {RequirementId}",
                    requirement.GlobalId ?? requirement.Item ?? "Unknown");
            }

            return jamaTestCases;
        }

        /// <summary>
        /// Convert requirements with test cases (both AI-generated and saved) to Jama test case format
        /// </summary>
        public List<JamaTestCaseRequest> ConvertAllTestCasesToJamaFormat(List<Requirement> requirements)
        {
            var jamaTestCases = new List<JamaTestCaseRequest>();

            foreach (var req in requirements)
            {
                try
                {
                    // Handle AI-generated test cases (from CurrentResponse.Output)
                    if (!string.IsNullOrWhiteSpace(req.CurrentResponse?.Output))
                    {
                        var aiTestCase = ConvertAIGeneratedTestCase(req);
                        if (aiTestCase != null)
                        {
                            jamaTestCases.Add(aiTestCase);
                        }
                    }

                    // Handle manually saved test cases (from GeneratedTestCases)
                    if (req.GeneratedTestCases?.Any() == true)
                    {
                        foreach (var savedTestCase in req.GeneratedTestCases)
                        {
                            var jamaTestCase = ConvertSavedTestCase(savedTestCase, req);
                            if (jamaTestCase != null)
                            {
                                jamaTestCases.Add(jamaTestCase);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error converting test cases for requirement {RequirementId}", 
                        req.GlobalId ?? req.Item ?? "Unknown");
                }
            }

            return jamaTestCases;
        }

        /// <summary>
        /// Convert an AI-generated test case from requirement output to Jama format
        /// </summary>
        public JamaTestCaseRequest? ConvertAIGeneratedTestCase(Requirement requirement)
        {
            var output = requirement.CurrentResponse?.Output;
            if (string.IsNullOrWhiteSpace(output)) return null;

            // Parse test steps from the requirement output
            var steps = ParseTestStepsFromOutput(output);
            if (!steps.Any())
            {
                // If no structured steps found, create a basic test case from the output
                steps = new List<JamaTestStep>
                {
                    new JamaTestStep
                    {
                        Number = "1",
                        Action = output.Length > 200 ? output.Substring(0, 200) + "..." : output,
                        ExpectedResult = "Verify test completes successfully",
                        Notes = ""
                    }
                };
            }

            var baseName = string.IsNullOrWhiteSpace(requirement.Name)
                ? (string.IsNullOrWhiteSpace(requirement.Item) ? "AI Generated Test Case" : $"{requirement.Item} – AI Generated Test")
                : $"{requirement.Item}: {requirement.Name}";

            var description = ExtractDescriptionFromOutput(output);
            if (string.IsNullOrWhiteSpace(description))
                description = requirement.Description ?? string.Empty;

            return new JamaTestCaseRequest
            {
                Name = baseName,
                Description = description,
                Steps = steps,
                AssociatedRequirements = requirement.GlobalId ?? requirement.Item ?? "",
                Tags = string.Join(";", requirement.TagList ?? Enumerable.Empty<string>()),
                Priority = "Medium",
                TestType = "Functional"
            };
        }

        /// <summary>
        /// Convert a saved test case to Jama format
        /// </summary>
        public JamaTestCaseRequest? ConvertSavedTestCase(TestCase savedTestCase, Requirement requirement)
        {
            if (savedTestCase == null) return null;

            var steps = new List<JamaTestStep>();

            // Convert test case steps to Jama format
            if (savedTestCase.Steps?.Any() == true)
            {
                for (int i = 0; i < savedTestCase.Steps.Count; i++)
                {
                    var step = savedTestCase.Steps[i];
                    steps.Add(new JamaTestStep
                    {
                        Number = step.StepNumber > 0 ? step.StepNumber.ToString() : (i + 1).ToString(),
                        Action = step.StepAction ?? $"Execute step {i + 1}",
                        ExpectedResult = step.StepExpectedResult ?? "Verify step completes successfully",
                        Notes = step.StepNotes ?? ""
                    });
                }
            }
            else if (!string.IsNullOrWhiteSpace(savedTestCase.StepAction))
            {
                // Handle single-step test case
                steps.Add(new JamaTestStep
                {
                    Number = savedTestCase.StepNumber ?? "1",
                    Action = savedTestCase.StepAction,
                    ExpectedResult = savedTestCase.StepExpectedResult ?? "Verify step completes successfully",
                    Notes = savedTestCase.StepNotes ?? ""
                });
            }
            else
            {
                // Create a basic step if no detailed steps exist
                steps.Add(new JamaTestStep
                {
                    Number = "1",
                    Action = savedTestCase.TestCaseText ?? savedTestCase.Name ?? "Execute test case",
                    ExpectedResult = "Verify test completes successfully",
                    Notes = ""
                });
            }

            return new JamaTestCaseRequest
            {
                Name = savedTestCase.Name ?? $"Test Case {savedTestCase.Id}",
                Description = savedTestCase.TestCaseText ?? "",
                Steps = steps,
                AssociatedRequirements = requirement.GlobalId ?? requirement.Item ?? "",
                Tags = savedTestCase.Tags ?? string.Join(";", requirement.TagList ?? Enumerable.Empty<string>()),
                Priority = "Medium", // TestCase model doesn't seem to have a Priority property
                TestType = "Functional"
            };
        }

        /// <summary>
        /// Parse test steps from requirement output text
        /// </summary>
        public List<JamaTestStep> ParseTestStepsFromOutput(string output)
        {
            var steps = new List<JamaTestStep>();

            try
            {
                // This is a simplified version - we would need to expose the step parsing methods
                // from RequirementService or duplicate the logic here
                var lines = output.Replace("\r", "").Split('\n');
                var stepNumber = 1;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    // Look for numbered steps or bullet points
                    if (trimmed.StartsWith($"{stepNumber}.") || trimmed.StartsWith("•") || trimmed.StartsWith("-"))
                    {
                        var stepText = trimmed.TrimStart('1', '2', '3', '4', '5', '6', '7', '8', '9', '0', '.', '•', '-').Trim();

                        if (!string.IsNullOrWhiteSpace(stepText))
                        {
                            steps.Add(new JamaTestStep
                            {
                                Number = stepNumber.ToString(),
                                Action = stepText,
                                ExpectedResult = "Verify step completes successfully",
                                Notes = ""
                            });
                            stepNumber++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error parsing test steps from output");
            }

            return steps;
        }

        /// <summary>
        /// Extract description/objective from requirement output
        /// </summary>
        public string ExtractDescriptionFromOutput(string output)
        {
            try
            {
                var lines = output.Replace("\r", "").Split('\n');

                // Look for "Objective:" or "Description:" sections
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Objective:", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
                    {
                        var colonIndex = trimmed.IndexOf(':');
                        if (colonIndex > 0 && colonIndex < trimmed.Length - 1)
                        {
                            return trimmed.Substring(colonIndex + 1).Trim();
                        }
                    }
                }

                // If no specific section found, use first non-empty line
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed) && 
                        !trimmed.StartsWith("#") && 
                        !trimmed.StartsWith("**"))
                    {
                        return trimmed;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error extracting description from output");
            }

            return string.Empty;
        }
    }
}