using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Envelope Schema Management Service
    /// Manages predefined and custom envelope schemas for LLM output parsing
    /// ARCHITECTURAL COMPLIANCE: Sealed class with interface, constructor injection, immutable state
    /// </summary>
    public sealed class EnvelopeSchemaService : IEnvelopeSchemaService
    {
        private readonly Dictionary<string, EnvelopeSchema> _registeredSchemas = new();
        private readonly object _schemaLock = new object();

        public EnvelopeSchemaService()
        {
            InitializePredefinedSchemas();
        }

        public EnvelopeSchema? GetSchema(string schemaName)
        {
            if (string.IsNullOrWhiteSpace(schemaName))
                return null;

            lock (_schemaLock)
            {
                return _registeredSchemas.TryGetValue(schemaName, out var schema) ? schema : null;
            }
        }

        public IReadOnlyCollection<string> GetAvailableSchemas()
        {
            lock (_schemaLock)
            {
                return _registeredSchemas.Keys.ToList().AsReadOnly();
            }
        }

        public async Task<EnvelopeSchema> CreateDynamicSchemaAsync(SchemaRequirements requirements)
        {
            var schema = new EnvelopeSchema
            {
                SchemaName = requirements.SchemaName,
                Description = requirements.Description,
                Version = "1.0",
                TargetEnvelopeType = requirements.TargetType,
                DefaultRepairStrategy = requirements.RepairStrategy,
                AllowCustomFields = requirements.AllowCustomFields
            };

            // Convert required fields
            foreach (var fieldReq in requirements.RequiredFields)
            {
                schema.RequiredFields.Add(new EnvelopeField
                {
                    FieldName = fieldReq.FieldName,
                    DisplayName = fieldReq.DisplayName,
                    Description = fieldReq.Description,
                    DataType = fieldReq.DataType,
                    MinLength = fieldReq.MinLength,
                    MaxLength = fieldReq.MaxLength,
                    RegexPattern = fieldReq.RegexPattern,
                    AllowedValues = fieldReq.AllowedValues,
                    DefaultValue = fieldReq.DefaultValue,
                    IsRequired = true
                });
            }

            // Convert optional fields
            foreach (var fieldReq in requirements.OptionalFields)
            {
                schema.OptionalFields.Add(new EnvelopeField
                {
                    FieldName = fieldReq.FieldName,
                    DisplayName = fieldReq.DisplayName,
                    Description = fieldReq.Description,
                    DataType = fieldReq.DataType,
                    MinLength = fieldReq.MinLength,
                    MaxLength = fieldReq.MaxLength,
                    RegexPattern = fieldReq.RegexPattern,
                    AllowedValues = fieldReq.AllowedValues,
                    DefaultValue = fieldReq.DefaultValue,
                    IsRequired = false
                });
            }

            // Add custom validation rules
            foreach (var ruleDescription in requirements.CustomValidationRules)
            {
                schema.ValidationRules.Add(new EnvelopeValidation
                {
                    RuleName = $"Custom_{Guid.NewGuid():N}",
                    Description = ruleDescription,
                    ValidationFunction = CreateCustomValidationFunction(ruleDescription),
                    ErrorMessage = $"Custom validation failed: {ruleDescription}",
                    Severity = EnvelopeValidationSeverity.Major,
                    SuggestedFix = "Review the validation rule requirements"
                });
            }

            return await Task.FromResult(schema);
        }

        public bool RegisterSchema(string schemaName, EnvelopeSchema schema)
        {
            if (string.IsNullOrWhiteSpace(schemaName) || schema == null)
                return false;

            lock (_schemaLock)
            {
                if (_registeredSchemas.ContainsKey(schemaName))
                    return false;

                _registeredSchemas[schemaName] = schema;
                return true;
            }
        }

        public EnvelopeSchema GetDefaultSchemaForType(EnvelopeType envelopeType)
        {
            return envelopeType switch
            {
                EnvelopeType.RequirementGeneration => GetSchema("RequirementGeneration") ?? CreateBasicSchema(),
                EnvelopeType.TestCaseGeneration => GetSchema("TestCaseGeneration") ?? CreateBasicSchema(),
                EnvelopeType.AnalysisResponse => GetSchema("AnalysisResponse") ?? CreateBasicSchema(),
                EnvelopeType.GeneralStructured => GetSchema("GeneralStructured") ?? CreateBasicSchema(),
                EnvelopeType.ErrorResponse => GetSchema("ErrorResponse") ?? CreateBasicSchema(),
                _ => CreateBasicSchema()
            };
        }

        public SchemaValidationResult ValidateSchema(EnvelopeSchema schema)
        {
            var result = new SchemaValidationResult
            {
                IsValid = true,
                ConfigurationScore = 1.0
            };

            if (schema == null)
            {
                result.IsValid = false;
                result.ValidationErrors.Add("Schema cannot be null");
                result.ConfigurationScore = 0.0;
                return result;
            }

            // Validate basic properties
            if (string.IsNullOrWhiteSpace(schema.SchemaName))
            {
                result.ValidationErrors.Add("Schema must have a name");
                result.IsValid = false;
            }

            if (string.IsNullOrWhiteSpace(schema.Version))
            {
                result.ValidationWarnings.Add("Schema should have a version identifier");
                result.ConfigurationScore -= 0.1;
            }

            // Validate required fields
            if (!schema.RequiredFields.Any())
            {
                result.ValidationWarnings.Add("Schema has no required fields - consider if this is intentional");
                result.ConfigurationScore -= 0.1;
            }

            var allFieldNames = new HashSet<string>();
            
            // Check for duplicate field names across required fields
            foreach (var field in schema.RequiredFields)
            {
                if (string.IsNullOrWhiteSpace(field.FieldName))
                {
                    result.ValidationErrors.Add("Required field must have a field name");
                    result.IsValid = false;
                    continue;
                }

                if (!allFieldNames.Add(field.FieldName))
                {
                    result.ValidationErrors.Add($"Duplicate field name: {field.FieldName}");
                    result.IsValid = false;
                }

                ValidateField(field, result);
            }

            // Check for duplicate field names across optional fields
            foreach (var field in schema.OptionalFields)
            {
                if (string.IsNullOrWhiteSpace(field.FieldName))
                {
                    result.ValidationErrors.Add("Optional field must have a field name");
                    result.IsValid = false;
                    continue;
                }

                if (!allFieldNames.Add(field.FieldName))
                {
                    result.ValidationErrors.Add($"Duplicate field name: {field.FieldName}");
                    result.IsValid = false;
                }

                ValidateField(field, result);
            }

            // Validate validation rules
            foreach (var rule in schema.ValidationRules)
            {
                if (string.IsNullOrWhiteSpace(rule.RuleName))
                {
                    result.ValidationErrors.Add("Validation rule must have a name");
                    result.IsValid = false;
                }

                if (rule.ValidationFunction == null)
                {
                    result.ValidationErrors.Add($"Validation rule '{rule.RuleName}' must have a validation function");
                    result.IsValid = false;
                }
            }

            // Calculate final score
            if (result.ValidationErrors.Any())
            {
                result.ConfigurationScore = Math.Max(0.0, result.ConfigurationScore - (result.ValidationErrors.Count * 0.2));
            }

            if (result.ValidationWarnings.Any())
            {
                result.ConfigurationScore = Math.Max(0.0, result.ConfigurationScore - (result.ValidationWarnings.Count * 0.05));
            }

            // Add recommendations
            if (result.ConfigurationScore < 0.8)
            {
                result.Recommendations.Add("Consider improving schema configuration based on validation errors and warnings");
            }

            if (schema.RequiredFields.Count > 10)
            {
                result.Recommendations.Add("Schema has many required fields - consider if some could be optional");
            }

            return result;
        }

        public bool UpdateSchema(string schemaName, EnvelopeSchema schema)
        {
            if (string.IsNullOrWhiteSpace(schemaName) || schema == null)
                return false;

            lock (_schemaLock)
            {
                if (!_registeredSchemas.ContainsKey(schemaName))
                    return false;

                _registeredSchemas[schemaName] = schema;
                return true;
            }
        }

        public bool RemoveSchema(string schemaName)
        {
            if (string.IsNullOrWhiteSpace(schemaName))
                return false;

            // Don't allow removal of core schemas
            var coreSchemas = new[] { "RequirementGeneration", "TestCaseGeneration", "AnalysisResponse", "GeneralStructured", "ErrorResponse" };
            if (coreSchemas.Contains(schemaName))
                return false;

            lock (_schemaLock)
            {
                return _registeredSchemas.Remove(schemaName);
            }
        }

        // Private helper methods

        private void InitializePredefinedSchemas()
        {
            // Register core schemas
            _registeredSchemas["RequirementGeneration"] = CreateRequirementGenerationSchema();
            _registeredSchemas["TestCaseGeneration"] = CreateTestCaseGenerationSchema();
            _registeredSchemas["AnalysisResponse"] = CreateAnalysisResponseSchema();
            _registeredSchemas["GeneralStructured"] = CreateGeneralStructuredSchema();
            _registeredSchemas["ErrorResponse"] = CreateErrorResponseSchema();
        }

        private EnvelopeSchema CreateRequirementGenerationSchema()
        {
            return new EnvelopeSchema
            {
                SchemaName = "RequirementGeneration",
                Description = "Schema for LLM-generated requirement responses",
                Version = "1.0",
                TargetEnvelopeType = EnvelopeType.RequirementGeneration,
                DefaultRepairStrategy = EnvelopeRepairStrategy.GracefulDegradation,
                RequiredFields = new List<EnvelopeField>
                {
                    new EnvelopeField
                    {
                        FieldName = "requirementId",
                        DisplayName = "Requirement ID",
                        Description = "Unique identifier for the generated requirement",
                        DataType = "string",
                        MinLength = 3,
                        MaxLength = 50,
                        RegexPattern = @"^REQ-[A-Z0-9]+-\d+$",
                        IsRequired = true
                    },
                    new EnvelopeField
                    {
                        FieldName = "title",
                        DisplayName = "Requirement Title",
                        Description = "Clear, concise title of the requirement",
                        DataType = "string",
                        MinLength = 10,
                        MaxLength = 200,
                        IsRequired = true
                    },
                    new EnvelopeField
                    {
                        FieldName = "description",
                        DisplayName = "Requirement Description",
                        Description = "Detailed description of the requirement",
                        DataType = "string",
                        MinLength = 50,
                        MaxLength = 2000,
                        IsRequired = true
                    },
                    new EnvelopeField
                    {
                        FieldName = "priority",
                        DisplayName = "Priority Level",
                        Description = "Priority level of the requirement",
                        DataType = "string",
                        AllowedValues = new List<string> { "Critical", "High", "Medium", "Low" },
                        DefaultValue = "Medium",
                        IsRequired = true
                    }
                },
                OptionalFields = new List<EnvelopeField>
                {
                    new EnvelopeField
                    {
                        FieldName = "category",
                        DisplayName = "Category",
                        Description = "Category classification of the requirement",
                        DataType = "string",
                        MaxLength = 100,
                        IsRequired = false
                    },
                    new EnvelopeField
                    {
                        FieldName = "acceptanceCriteria",
                        DisplayName = "Acceptance Criteria",
                        Description = "List of acceptance criteria for the requirement",
                        DataType = "array",
                        IsRequired = false
                    },
                    new EnvelopeField
                    {
                        FieldName = "tags",
                        DisplayName = "Tags",
                        Description = "Comma-separated tags for the requirement",
                        DataType = "array",
                        IsRequired = false
                    }
                }
            };
        }

        private EnvelopeSchema CreateTestCaseGenerationSchema()
        {
            return new EnvelopeSchema
            {
                SchemaName = "TestCaseGeneration",
                Description = "Schema for LLM-generated test case responses",
                Version = "1.0",
                TargetEnvelopeType = EnvelopeType.TestCaseGeneration,
                DefaultRepairStrategy = EnvelopeRepairStrategy.GracefulDegradation,
                RequiredFields = new List<EnvelopeField>
                {
                    new EnvelopeField
                    {
                        FieldName = "testCaseId",
                        DisplayName = "Test Case ID",
                        Description = "Unique identifier for the test case",
                        DataType = "string",
                        MinLength = 3,
                        MaxLength = 50,
                        RegexPattern = @"^TC-[A-Z0-9]+-\d+$",
                        IsRequired = true
                    },
                    new EnvelopeField
                    {
                        FieldName = "title",
                        DisplayName = "Test Case Title",
                        Description = "Clear, descriptive title of the test case",
                        DataType = "string",
                        MinLength = 10,
                        MaxLength = 200,
                        IsRequired = true
                    },
                    new EnvelopeField
                    {
                        FieldName = "objective",
                        DisplayName = "Test Objective",
                        Description = "What this test case aims to verify",
                        DataType = "string",
                        MinLength = 20,
                        MaxLength = 500,
                        IsRequired = true
                    },
                    new EnvelopeField
                    {
                        FieldName = "steps",
                        DisplayName = "Test Steps",
                        Description = "Step-by-step test procedure",
                        DataType = "array",
                        IsRequired = true
                    },
                    new EnvelopeField
                    {
                        FieldName = "expectedResults",
                        DisplayName = "Expected Results",
                        Description = "Expected outcomes for each test step",
                        DataType = "array",
                        IsRequired = true
                    }
                },
                OptionalFields = new List<EnvelopeField>
                {
                    new EnvelopeField
                    {
                        FieldName = "prerequisites",
                        DisplayName = "Prerequisites",
                        Description = "Conditions that must be met before running the test",
                        DataType = "array",
                        IsRequired = false
                    },
                    new EnvelopeField
                    {
                        FieldName = "testData",
                        DisplayName = "Test Data",
                        Description = "Data required for test execution",
                        DataType = "object",
                        IsRequired = false
                    },
                    new EnvelopeField
                    {
                        FieldName = "priority",
                        DisplayName = "Test Priority",
                        Description = "Priority level of the test case",
                        DataType = "string",
                        AllowedValues = new List<string> { "Critical", "High", "Medium", "Low" },
                        DefaultValue = "Medium",
                        IsRequired = false
                    }
                }
            };
        }

        private EnvelopeSchema CreateAnalysisResponseSchema()
        {
            return new EnvelopeSchema
            {
                SchemaName = "AnalysisResponse",
                Description = "Schema for LLM analysis and evaluation responses",
                Version = "1.0",
                TargetEnvelopeType = EnvelopeType.AnalysisResponse,
                DefaultRepairStrategy = EnvelopeRepairStrategy.GracefulDegradation,
                RequiredFields = new List<EnvelopeField>
                {
                    new EnvelopeField
                    {
                        FieldName = "analysisType",
                        DisplayName = "Analysis Type",
                        Description = "Type of analysis performed",
                        DataType = "string",
                        AllowedValues = new List<string> { "Quality", "Completeness", "Consistency", "Coverage", "Risk", "Other" },
                        IsRequired = true
                    },
                    new EnvelopeField
                    {
                        FieldName = "findings",
                        DisplayName = "Key Findings",
                        Description = "Primary findings from the analysis",
                        DataType = "array",
                        IsRequired = true
                    },
                    new EnvelopeField
                    {
                        FieldName = "overallScore",
                        DisplayName = "Overall Score",
                        Description = "Numerical score or rating (0-100)",
                        DataType = "number",
                        IsRequired = true
                    },
                    new EnvelopeField
                    {
                        FieldName = "summary",
                        DisplayName = "Executive Summary",
                        Description = "High-level summary of the analysis",
                        DataType = "string",
                        MinLength = 50,
                        MaxLength = 1000,
                        IsRequired = true
                    }
                },
                OptionalFields = new List<EnvelopeField>
                {
                    new EnvelopeField
                    {
                        FieldName = "recommendations",
                        DisplayName = "Recommendations",
                        Description = "Actionable recommendations based on analysis",
                        DataType = "array",
                        IsRequired = false
                    },
                    new EnvelopeField
                    {
                        FieldName = "risks",
                        DisplayName = "Identified Risks",
                        Description = "Risks identified during analysis",
                        DataType = "array",
                        IsRequired = false
                    },
                    new EnvelopeField
                    {
                        FieldName = "confidence",
                        DisplayName = "Confidence Level",
                        Description = "Confidence in the analysis (0-100)",
                        DataType = "number",
                        IsRequired = false
                    }
                }
            };
        }

        private EnvelopeSchema CreateGeneralStructuredSchema()
        {
            return new EnvelopeSchema
            {
                SchemaName = "GeneralStructured",
                Description = "General-purpose schema for structured LLM responses",
                Version = "1.0",
                TargetEnvelopeType = EnvelopeType.GeneralStructured,
                DefaultRepairStrategy = EnvelopeRepairStrategy.BestEffortRecovery,
                AllowCustomFields = true,
                RequiredFields = new List<EnvelopeField>
                {
                    new EnvelopeField
                    {
                        FieldName = "responseType",
                        DisplayName = "Response Type",
                        Description = "Type or category of the response",
                        DataType = "string",
                        MinLength = 3,
                        MaxLength = 100,
                        IsRequired = true
                    },
                    new EnvelopeField
                    {
                        FieldName = "content",
                        DisplayName = "Main Content",
                        Description = "Primary content of the response",
                        DataType = "string",
                        MinLength = 1,
                        IsRequired = true
                    }
                },
                OptionalFields = new List<EnvelopeField>
                {
                    new EnvelopeField
                    {
                        FieldName = "metadata",
                        DisplayName = "Metadata",
                        Description = "Additional metadata about the response",
                        DataType = "object",
                        IsRequired = false
                    },
                    new EnvelopeField
                    {
                        FieldName = "confidence",
                        DisplayName = "Confidence Score",
                        Description = "Confidence in the response (0-100)",
                        DataType = "number",
                        IsRequired = false
                    }
                }
            };
        }

        private EnvelopeSchema CreateErrorResponseSchema()
        {
            return new EnvelopeSchema
            {
                SchemaName = "ErrorResponse",
                Description = "Schema for error responses and fallback scenarios",
                Version = "1.0",
                TargetEnvelopeType = EnvelopeType.ErrorResponse,
                DefaultRepairStrategy = EnvelopeRepairStrategy.FallbackToRaw,
                RequiredFields = new List<EnvelopeField>
                {
                    new EnvelopeField
                    {
                        FieldName = "errorType",
                        DisplayName = "Error Type",
                        Description = "Type of error that occurred",
                        DataType = "string",
                        AllowedValues = new List<string> { "ParseError", "ValidationError", "ProcessingError", "UnknownError" },
                        DefaultValue = "UnknownError",
                        IsRequired = true
                    },
                    new EnvelopeField
                    {
                        FieldName = "errorMessage",
                        DisplayName = "Error Message",
                        Description = "Human-readable description of the error",
                        DataType = "string",
                        MinLength = 10,
                        MaxLength = 500,
                        IsRequired = true
                    }
                },
                OptionalFields = new List<EnvelopeField>
                {
                    new EnvelopeField
                    {
                        FieldName = "rawResponse",
                        DisplayName = "Raw Response",
                        Description = "Original unprocessed response",
                        DataType = "string",
                        IsRequired = false
                    },
                    new EnvelopeField
                    {
                        FieldName = "recoveryActions",
                        DisplayName = "Recovery Actions",
                        Description = "Suggested actions to recover from the error",
                        DataType = "array",
                        IsRequired = false
                    }
                }
            };
        }

        private EnvelopeSchema CreateBasicSchema()
        {
            return CreateGeneralStructuredSchema();
        }

        private void ValidateField(EnvelopeField field, SchemaValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(field.DisplayName))
            {
                result.ValidationWarnings.Add($"Field '{field.FieldName}' should have a display name");
                result.ConfigurationScore -= 0.02;
            }

            if (string.IsNullOrWhiteSpace(field.Description))
            {
                result.ValidationWarnings.Add($"Field '{field.FieldName}' should have a description");
                result.ConfigurationScore -= 0.02;
            }

            if (field.MinLength.HasValue && field.MaxLength.HasValue && field.MinLength > field.MaxLength)
            {
                result.ValidationErrors.Add($"Field '{field.FieldName}' has invalid length constraints (min > max)");
                result.IsValid = false;
            }

            if (!string.IsNullOrEmpty(field.RegexPattern))
            {
                try
                {
                    _ = new System.Text.RegularExpressions.Regex(field.RegexPattern);
                }
                catch (ArgumentException)
                {
                    result.ValidationErrors.Add($"Field '{field.FieldName}' has invalid regex pattern");
                    result.IsValid = false;
                }
            }
        }

        private Func<OutputEnvelope, ValidationResult> CreateCustomValidationFunction(string ruleDescription)
        {
            // For dynamic schema creation, create a basic validation function
            // In a production system, this could be more sophisticated with rule parsing
            return envelope =>
            {
                // Basic validation: check that envelope has structured data
                if (envelope.StructuredData == null)
                {
                    return new ValidationResult 
                    { 
                        IsValid = false, 
                        ErrorMessage = "No structured data available for custom validation",
                        Severity = ValidationSeverity.Error
                    };
                }

                // For now, always pass custom validations in dynamic schemas
                // TODO: Implement rule parsing for more sophisticated custom validation
                return new ValidationResult 
                { 
                    IsValid = true, 
                    ErrorMessage = null,
                    Severity = ValidationSeverity.Info
                };
            };
        }
    }
}