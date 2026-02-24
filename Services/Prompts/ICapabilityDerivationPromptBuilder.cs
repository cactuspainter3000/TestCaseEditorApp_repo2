using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Services.Prompts
{
    /// <summary>
    /// Interface for building prompts for ATP capability derivation using A-N taxonomy
    /// </summary>
    public interface ICapabilityDerivationPromptBuilder
    {
        /// <summary>
        /// Gets the system prompt for ATP capability derivation
        /// </summary>
        string GetSystemPrompt();

        /// <summary>
        /// Builds a derivation prompt for ATP steps using A-N taxonomy guidance
        /// </summary>
        string BuildDerivationPrompt(
            string atpStep,
            ParsedATPStep stepMetadata = null,
            string systemType = "Generic",
            DerivationOptions derivationOptions = null);
    }
}