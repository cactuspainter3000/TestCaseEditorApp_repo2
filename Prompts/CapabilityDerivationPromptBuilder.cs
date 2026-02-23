using System;
using System.Collections.Generic;
using System.Text;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Prompts
{
    /// <summary>
    /// Builds specialized prompts for LLM-powered ATP capability derivation.
    /// Transforms test procedure steps into system requirements using A-N taxonomy classification.
    /// </summary>
    public sealed class CapabilityDerivationPromptBuilder
    {
        private readonly SystemRequirementTaxonomy _taxonomy;

        public CapabilityDerivationPromptBuilder()
        {
            _taxonomy = SystemRequirementTaxonomy.Default;
        }

        /// <summary>
        /// Get the system message that establishes ATP derivation context and methodology.
        /// </summary>
        public string GetSystemPrompt()
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are a systems engineering expert specializing in capability derivation from test procedures.");
            sb.AppendLine("Your task is to analyze ATP (Acceptance Test Procedure) steps and derive the underlying SYSTEM CAPABILITIES required.");
            sb.AppendLine();
            sb.AppendLine("CORE MISSION: Transform 'what we test' into 'what the system must provide'");
            sb.AppendLine();
            sb.AppendLine("CRITICAL DISTINCTION:");
            sb.AppendLine("- ATP steps describe TEST ACTIONS (what testers do)");
            sb.AppendLine("- System capabilities describe SYSTEM ABILITIES (what the system must be capable of)");
            sb.AppendLine("- You derive the capabilities needed to ENABLE successful execution of the test");
            sb.AppendLine();

            // Add taxonomy reference
            sb.AppendLine("TAXONOMY FRAMEWORK (A-N Categories):");
            sb.AppendLine();
            foreach (var category in _taxonomy.Categories)
            {
                sb.AppendLine($"{category.Code}. {category.Name}");
                sb.AppendLine($"   {category.Description}");
                sb.AppendLine();
            }

            // Derivation methodology
            sb.AppendLine("DERIVATION METHODOLOGY:");
            sb.AppendLine("1. Identify what SYSTEM FUNCTIONS must exist for this test step to be executable");
            sb.AppendLine("2. Classify each capability using A-N taxonomy (e.g., C1, D3, G1)");
            sb.AppendLine("3. Write as 'System shall provide...' or 'System shall be capable of...'");
            sb.AppendLine("4. Mark missing specifications as [TBD] with clear context");
            sb.AppendLine();

            // Anti-patterns
            sb.AppendLine("CRITICAL ANTI-PATTERNS TO AVOID:");
            sb.AppendLine("- DO NOT derive test procedures or operator actions");
            sb.AppendLine("- DO NOT derive compliance statements");
            sb.AppendLine("- DO NOT invent technical specifications not mentioned");
            sb.AppendLine();

            // Output format
            sb.AppendLine("REQUIRED OUTPUT FORMAT:");
            sb.AppendLine("Respond with ONLY valid JSON in this structure:");
            sb.AppendLine();
            sb.AppendLine(GetJsonFormatExample());

            return sb.ToString();
        }

        /// <summary>
        /// Build a capability derivation prompt for a single ATP step
        /// </summary>
        public string BuildDerivationPrompt(
            string atpStep, 
            ParsedATPStep stepMetadata = null, 
            string systemType = "Generic",
            DerivationOptions derivationOptions = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine("ATP STEP ANALYSIS REQUEST");
            sb.AppendLine("=========================");
            sb.AppendLine();

            sb.AppendLine($"SYSTEM TYPE: {systemType}");
            sb.AppendLine();

            sb.AppendLine("ATP STEP TO ANALYZE:");
            sb.AppendLine(atpStep);
            sb.AppendLine();

            if (stepMetadata != null)
            {
                sb.AppendLine("STEP METADATA:");
                if (!string.IsNullOrEmpty(stepMetadata.StepNumber))
                    sb.AppendLine($"Step Number: {stepMetadata.StepNumber}");
                sb.AppendLine($"Step Type: {stepMetadata.StepType}");
                sb.AppendLine();
            }

            sb.AppendLine("ANALYSIS FOCUS:");
            sb.AppendLine("1. What system interfaces must exist?");
            sb.AppendLine("2. What measurement or control functions are needed?");
            sb.AppendLine("3. What data handling capabilities are required?");
            sb.AppendLine("4. What performance characteristics must be provided?");
            sb.AppendLine();

            sb.AppendLine("RESPOND WITH JSON:");
            sb.AppendLine("Use the exact JSON structure defined in the system message.");

            return sb.ToString();
        }

        /// <summary>
        /// Build validation prompt for reviewing derived capabilities
        /// </summary>
        public string BuildValidationPrompt(List<DerivedCapability> derivedCapabilities, string originalAtpContent)
        {
            var sb = new StringBuilder();

            sb.AppendLine("CAPABILITY VALIDATION REVIEW");
            sb.AppendLine("============================");
            sb.AppendLine();
            sb.AppendLine("TASK: Review previously derived capabilities for quality and completeness");
            sb.AppendLine();

            sb.AppendLine($"REVIEWING {derivedCapabilities.Count} DERIVED CAPABILITIES");
            sb.AppendLine();

            sb.AppendLine("VALIDATION CRITERIA:");
            sb.AppendLine("1. Completeness: Are key system capabilities missing?");
            sb.AppendLine("2. Accuracy: Do capabilities correctly reflect ATP requirements?");
            sb.AppendLine("3. Taxonomy: Are A-N classifications correct?");
            sb.AppendLine("4. Clarity: Are capability statements implementable?");
            sb.AppendLine();

            sb.AppendLine("RESPOND WITH validation assessment and recommendations.");

            return sb.ToString();
        }

        private string GetJsonFormatExample()
        {
            // Use simple string concatenation to avoid formatting issues
            var json = "{" + Environment.NewLine +
                "  \"derivedCapabilities\": [" + Environment.NewLine +
                "    {" + Environment.NewLine +
                "      \"requirementText\": \"System shall provide voltage measurement capability\"," + Environment.NewLine +
                "      \"taxonomyCategory\": \"D\"," + Environment.NewLine +
                "      \"taxonomySubcategory\": \"D1\"," + Environment.NewLine +
                "      \"derivationRationale\": \"ATP step requires voltage measurement\"," + Environment.NewLine +
                "      \"missingSpecifications\": [\"accuracy\", \"range\"]," + Environment.NewLine +
                "      \"allocationTargets\": [\"InstrumentationSubsystem\"]," + Environment.NewLine +
                "      \"confidenceScore\": 0.85" + Environment.NewLine +
                "    }" + Environment.NewLine +
                "  ]," + Environment.NewLine +
                "  \"rejectedItems\": [" + Environment.NewLine +
                "    {" + Environment.NewLine +
                "      \"text\": \"Operator shall press button\"," + Environment.NewLine +
                "      \"reason\": \"Operator procedure, not system capability\"" + Environment.NewLine +
                "    }" + Environment.NewLine +
                "  ]" + Environment.NewLine +
                "}";
            
            return json;
        }
    }
}