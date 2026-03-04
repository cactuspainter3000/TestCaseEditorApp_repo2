using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Interface for Deterministic Output Envelope Service
    /// Provides standardized LLM output format with predictable structure parsing
    /// ARCHITECTURAL COMPLIANCE: Interface-first design for dependency injection
    /// </summary>
    public interface IOutputEnvelopeService
    {
        /// <summary>
        /// Creates a new envelope with basic metadata
        /// </summary>
        OutputEnvelope CreateEnvelope(string responseId, EnvelopeType type);

        /// <summary>
        /// Parse LLM response into structured envelope format
        /// </summary>
        Task<EnvelopeParseResult> ParseEnvelopeAsync(string llmResponse, EnvelopeSchema expectedSchema);

        /// <summary>
        /// Validate envelope against schema requirements
        /// </summary>
        EnvelopeValidationResult ValidateEnvelope(OutputEnvelope envelope, EnvelopeSchema schema);

        /// <summary>
        /// Extract typed data from validated envelope
        /// </summary>
        T ExtractData<T>(OutputEnvelope envelope) where T : class;

        /// <summary>
        /// Generate template instructions for LLM prompting
        /// </summary>
        string GenerateEnvelopeTemplate(EnvelopeSchema schema, string instructionContext);

        /// <summary>
        /// Repair malformed envelope with graceful degradation
        /// </summary>
        Task<EnvelopeRepairResult> RepairMalformedEnvelopeAsync(string malformedResponse, EnvelopeSchema expectedSchema);
    }
}