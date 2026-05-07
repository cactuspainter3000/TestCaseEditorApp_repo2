using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Interface for managing envelope schemas
    /// Provides schema definitions for different types of LLM output scenarios
    /// ARCHITECTURAL COMPLIANCE: Interface-first design for dependency injection
    /// </summary>
    public interface IEnvelopeSchemaService
    {
        /// <summary>
        /// Gets a predefined schema by name
        /// </summary>
        /// <param name="schemaName">Name of the schema to retrieve</param>
        /// <returns>The envelope schema, or null if not found</returns>
        EnvelopeSchema? GetSchema(string schemaName);

        /// <summary>
        /// Gets all available schema names
        /// </summary>
        /// <returns>Collection of available schema names</returns>
        IReadOnlyCollection<string> GetAvailableSchemas();

        /// <summary>
        /// Creates a dynamic schema based on requirements
        /// </summary>
        /// <param name="requirements">Schema requirements specification</param>
        /// <returns>Dynamically generated envelope schema</returns>
        Task<EnvelopeSchema> CreateDynamicSchemaAsync(SchemaRequirements requirements);

        /// <summary>
        /// Registers a custom schema for use in the application
        /// </summary>
        /// <param name="schemaName">Unique name for the schema</param>
        /// <param name="schema">Schema definition to register</param>
        /// <returns>True if registration successful, false if schema name already exists</returns>
        bool RegisterSchema(string schemaName, EnvelopeSchema schema);

        /// <summary>
        /// Gets the default schema for a specific envelope type
        /// </summary>
        /// <param name="envelopeType">Type of envelope</param>
        /// <returns>Default schema for the envelope type</returns>
        EnvelopeSchema GetDefaultSchemaForType(EnvelopeType envelopeType);

        /// <summary>
        /// Validates that a schema configuration is valid and complete
        /// </summary>
        /// <param name="schema">Schema to validate</param>
        /// <returns>Validation result with any issues found</returns>
        SchemaValidationResult ValidateSchema(EnvelopeSchema schema);

        /// <summary>
        /// Updates an existing schema
        /// </summary>
        /// <param name="schemaName">Name of the schema to update</param>
        /// <param name="schema">Updated schema definition</param>
        /// <returns>True if update successful, false if schema doesn't exist</returns>
        bool UpdateSchema(string schemaName, EnvelopeSchema schema);

        /// <summary>
        /// Removes a schema from the registry
        /// </summary>
        /// <param name="schemaName">Name of the schema to remove</param>
        /// <returns>True if removal successful, false if schema doesn't exist or cannot be removed</returns>
        bool RemoveSchema(string schemaName);
    }

    /// <summary>
    /// Requirements specification for dynamic schema creation
    /// </summary>
    public class SchemaRequirements
    {
        public string SchemaName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public EnvelopeType TargetType { get; set; } = EnvelopeType.GeneralStructured;
        public EnvelopeRepairStrategy RepairStrategy { get; set; } = EnvelopeRepairStrategy.GracefulDegradation;
        public List<DynamicFieldRequirement> RequiredFields { get; set; } = new();
        public List<DynamicFieldRequirement> OptionalFields { get; set; } = new();
        public List<string> CustomValidationRules { get; set; } = new();
        public bool AllowCustomFields { get; set; } = true;
    }

    /// <summary>
    /// Dynamic field requirement specification
    /// </summary>
    public class DynamicFieldRequirement
    {
        public string FieldName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DataType { get; set; } = "string"; // string, number, boolean, array, object
        public int? MinLength { get; set; }
        public int? MaxLength { get; set; }
        public string? RegexPattern { get; set; }
        public List<string> AllowedValues { get; set; } = new();
        public string? DefaultValue { get; set; }
        public bool IsRequired { get; set; } = true;
    }

    /// <summary>
    /// Result of schema validation
    /// </summary>
    public class SchemaValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> ValidationErrors { get; set; } = new();
        public List<string> ValidationWarnings { get; set; } = new();
        public double ConfigurationScore { get; set; } // 0.0 - 1.0
        public List<string> Recommendations { get; set; } = new();
    }
}