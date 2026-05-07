using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Deterministic Output Envelope Service
    /// Provides standardized LLM output format with predictable structure parsing
    /// ARCHITECTURAL COMPLIANCE: Sealed class with interface, constructor injection, no direct instantiation
    /// </summary>
    public sealed class OutputEnvelopeService : IOutputEnvelopeService
    {
        private readonly IEnvelopeSchemaService _schemaService;

        public OutputEnvelopeService(IEnvelopeSchemaService schemaService)
        {
            _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
        }

        public OutputEnvelope CreateEnvelope(string responseId, EnvelopeType type)
        {
            return new OutputEnvelope
            {
                EnvelopeId = Guid.NewGuid().ToString(),
                ResponseId = responseId,
                Type = type,
                CreatedAt = DateTime.UtcNow,
                Version = "1.0"
            };
        }

        public async Task<EnvelopeParseResult> ParseEnvelopeAsync(string llmResponse, EnvelopeSchema expectedSchema)
        {
            var startTime = DateTime.UtcNow;
            var result = new EnvelopeParseResult
            {
                Metrics = new EnvelopeParseMetrics(),
                UsedStrategy = expectedSchema.DefaultRepairStrategy
            };

            try
            {
                // Step 1: Try direct JSON parsing
                var envelope = await TryDirectParseAsync(llmResponse, expectedSchema);
                if (envelope != null)
                {
                    result.IsSuccessful = true;
                    result.ParsedEnvelope = envelope;
                    result.Metrics.SuccessRate = 1.0;
                }
                else
                {
                    // Step 2: Try repair strategies
                    var repairResult = await RepairMalformedEnvelopeAsync(llmResponse, expectedSchema);
                    result.IsSuccessful = repairResult.RepairSuccessful;
                    result.ParsedEnvelope = repairResult.RepairedEnvelope;
                    result.UsedStrategy = repairResult.StrategyUsed;
                    result.Metrics.SuccessRate = repairResult.RepairConfidence;
                    
                    if (!result.IsSuccessful)
                    {
                        result.ErrorMessage = repairResult.FailureReason;
                        result.FallbackData = llmResponse; // Preserve raw data
                    }
                }

                // Calculate metrics
                result.Metrics.ParseTime = DateTime.UtcNow - startTime;
                result.Metrics.FieldsParsed = result.ParsedEnvelope?.StructuredData?.RootElement.EnumerateObject().Count() ?? 0;
                
                return result;
            }
            catch (Exception ex)
            {
                result.IsSuccessful = false;
                result.ErrorMessage = $"Envelope parsing failed: {ex.Message}";
                result.FallbackData = llmResponse;
                result.Metrics.ParseTime = DateTime.UtcNow - startTime;
                return result;
            }
        }

        public EnvelopeValidationResult ValidateEnvelope(OutputEnvelope envelope, EnvelopeSchema schema)
        {
            var result = new EnvelopeValidationResult
            {
                Summary = new EnvelopeValidationSummary()
            };

            try
            {
                // Validate required fields
                foreach (var requiredField in schema.RequiredFields)
                {
                    result.Summary.TotalFields++;
                    
                    if (!HasField(envelope, requiredField.FieldName))
                    {
                        result.Errors.Add(new EnvelopeValidationError
                        {
                            Field = requiredField.FieldName,
                            ErrorCode = "MISSING_REQUIRED_FIELD",
                            Message = $"Required field '{requiredField.DisplayName}' is missing",
                            Severity = EnvelopeValidationSeverity.Critical,
                            SuggestedFix = $"Add field '{requiredField.FieldName}' to the response"
                        });
                        result.Summary.RequiredFieldsMissing++;
                        result.Summary.CriticalErrors++;
                    }
                    else
                    {
                        // Validate field content
                        ValidateFieldContent(envelope, requiredField, result);
                        result.Summary.ValidFields++;
                    }
                }

                // Validate optional fields if present
                foreach (var optionalField in schema.OptionalFields)
                {
                    result.Summary.TotalFields++;
                    
                    if (HasField(envelope, optionalField.FieldName))
                    {
                        ValidateFieldContent(envelope, optionalField, result);
                        result.Summary.ValidFields++;
                    }
                    else
                    {
                        result.Summary.OptionalFieldsMissing++;
                    }
                }

                // Apply custom validation rules
                foreach (var validationRule in schema.ValidationRules)
                {
                    var validationResult = validationRule.ValidationFunction(envelope);
                    if (!validationResult.IsValid)
                    {
                        var error = new EnvelopeValidationError
                        {
                            Field = validationRule.RuleName,
                            ErrorCode = "CUSTOM_VALIDATION_FAILED",
                            Message = validationRule.ErrorMessage,
                            Severity = validationRule.Severity,
                            SuggestedFix = validationRule.SuggestedFix
                        };
                        
                        if (validationRule.Severity == EnvelopeValidationSeverity.Critical || 
                            validationRule.Severity == EnvelopeValidationSeverity.Major)
                        {
                            result.Errors.Add(error);
                            if (validationRule.Severity == EnvelopeValidationSeverity.Critical)
                                result.Summary.CriticalErrors++;
                            else
                                result.Summary.MajorErrors++;
                        }
                        else
                        {
                            result.Warnings.Add(new EnvelopeValidationWarning
                            {
                                Field = error.Field,
                                Message = error.Message,
                                Recommendation = error.SuggestedFix
                            });
                            result.Summary.Warnings++;
                        }
                    }
                }

                // Calculate compliance score
                var totalPossibleFields = schema.RequiredFields.Count + schema.OptionalFields.Count;
                var validFieldsWeight = totalPossibleFields > 0 ? (double)result.Summary.ValidFields / totalPossibleFields : 1.0;
                var errorPenalty = Math.Max(0, 1.0 - (result.Summary.CriticalErrors * 0.3 + result.Summary.MajorErrors * 0.1));
                result.ComplianceScore = validFieldsWeight * errorPenalty;
                
                result.IsValid = result.Summary.CriticalErrors == 0 && result.Summary.MajorErrors == 0;

                return result;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add(new EnvelopeValidationError
                {
                    Field = "ENVELOPE_VALIDATION",
                    ErrorCode = "VALIDATION_EXCEPTION",
                    Message = $"Validation failed with exception: {ex.Message}",
                    Severity = EnvelopeValidationSeverity.Critical
                });
                return result;
            }
        }

        public T ExtractData<T>(OutputEnvelope envelope) where T : class
        {
            if (envelope.StructuredData == null)
                throw new InvalidOperationException("No structured data available in envelope");

            try
            {
                var jsonString = envelope.StructuredData.RootElement.GetRawText();
                var result = JsonSerializer.Deserialize<T>(jsonString);
                return result ?? throw new InvalidOperationException($"Failed to deserialize to type {typeof(T).Name}");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to extract data as {typeof(T).Name}: {ex.Message}");
            }
        }

        public string GenerateEnvelopeTemplate(EnvelopeSchema schema, string instructionContext)
        {
            var template = new System.Text.StringBuilder();
            
            template.AppendLine("RESPONSE FORMAT INSTRUCTIONS:");
            template.AppendLine("===========================");
            template.AppendLine();
            template.AppendLine("You must respond using the following JSON structure exactly:");
            template.AppendLine();
            template.AppendLine("{");
            
            // Required fields
            template.AppendLine("  // === REQUIRED FIELDS ===");
            foreach (var field in schema.RequiredFields)
            {
                template.AppendLine($"  \"{field.FieldName}\": \"{field.Description}\",");
            }
            
            // Optional fields
            if (schema.OptionalFields.Any())
            {
                template.AppendLine("  // === OPTIONAL FIELDS ===");
                foreach (var field in schema.OptionalFields)
                {
                    template.AppendLine($"  \"{field.FieldName}\": \"{field.Description}\",");
                }
            }
            
            template.AppendLine("}");
            template.AppendLine();
            template.AppendLine("IMPORTANT:");
            template.AppendLine("- Include ALL required fields");
            template.AppendLine("- Use exact field names as shown");
            template.AppendLine("- Provide meaningful values, not placeholder text");
            template.AppendLine($"- Context: {instructionContext}");
            
            return template.ToString();
        }

        public async Task<EnvelopeRepairResult> RepairMalformedEnvelopeAsync(string malformedResponse, EnvelopeSchema expectedSchema)
        {
            var result = new EnvelopeRepairResult
            {
                OriginalResponse = malformedResponse,
                StrategyUsed = expectedSchema.DefaultRepairStrategy
            };

            try
            {
                switch (expectedSchema.DefaultRepairStrategy)
                {
                    case EnvelopeRepairStrategy.StrictValidation:
                        // No repair attempts - must be perfect JSON
                        result.RepairSuccessful = false;
                        result.FailureReason = "Strict validation requires perfect JSON format";
                        break;
                        
                    case EnvelopeRepairStrategy.GracefulDegradation:
                        result = await AttemptGracefulRepair(malformedResponse, expectedSchema);
                        break;
                        
                    case EnvelopeRepairStrategy.BestEffortRecovery:
                        result = await AttemptBestEffortRecovery(malformedResponse, expectedSchema);
                        break;
                        
                    case EnvelopeRepairStrategy.FallbackToRaw:
                        result = CreateRawFallbackEnvelope(malformedResponse, expectedSchema);
                        break;
                }

                return result;
            }
            catch (Exception ex)
            {
                result.RepairSuccessful = false;
                result.FailureReason = $"Repair process failed: {ex.Message}";
                return result;
            }
        }

        // Private helper methods

        private async Task<OutputEnvelope?> TryDirectParseAsync(string llmResponse, EnvelopeSchema expectedSchema)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(llmResponse);
                var envelope = CreateEnvelope(Guid.NewGuid().ToString(), EnvelopeType.GeneralStructured);
                envelope.StructuredData = jsonDoc;
                envelope.RawResponse = llmResponse;
                
                // Quick validation
                var validation = ValidateEnvelope(envelope, expectedSchema);
                envelope.ValidationResult = validation;
                envelope.CompletenessScore = validation.ComplianceScore;
                
                return envelope;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private bool HasField(OutputEnvelope envelope, string fieldName)
        {
            return envelope.StructuredData?.RootElement.TryGetProperty(fieldName, out _) == true;
        }

        private void ValidateFieldContent(OutputEnvelope envelope, EnvelopeField field, EnvelopeValidationResult result)
        {
            if (envelope.StructuredData?.RootElement.TryGetProperty(field.FieldName, out var element) == true)
            {
                // Basic type validation
                var value = element.ToString();
                
                // Length validation
                if (field.MinLength.HasValue && value.Length < field.MinLength.Value)
                {
                    result.Errors.Add(new EnvelopeValidationError
                    {
                        Field = field.FieldName,
                        ErrorCode = "MIN_LENGTH_VIOLATION",
                        Message = $"Field '{field.DisplayName}' is too short (min: {field.MinLength.Value})",
                        Severity = EnvelopeValidationSeverity.Major,
                        ActualValue = value.Length,
                        ExpectedValue = field.MinLength.Value
                    });
                    result.Summary.MajorErrors++;
                }
                
                if (field.MaxLength.HasValue && value.Length > field.MaxLength.Value)
                {
                    result.Warnings.Add(new EnvelopeValidationWarning
                    {
                        Field = field.FieldName,
                        Message = $"Field '{field.DisplayName}' exceeds recommended length (max: {field.MaxLength.Value})",
                        Recommendation = "Consider shortening the content"
                    });
                    result.Summary.Warnings++;
                }

                // Regex validation
                if (!string.IsNullOrEmpty(field.RegexPattern))
                {
                    if (!Regex.IsMatch(value, field.RegexPattern))
                    {
                        result.Errors.Add(new EnvelopeValidationError
                        {
                            Field = field.FieldName,
                            ErrorCode = "REGEX_PATTERN_VIOLATION",
                            Message = $"Field '{field.DisplayName}' does not match required pattern",
                            Severity = EnvelopeValidationSeverity.Major,
                            ActualValue = value,
                            SuggestedFix = $"Ensure value matches pattern: {field.RegexPattern}"
                        });
                        result.Summary.MajorErrors++;
                    }
                }

                // Allowed values validation
                if (field.AllowedValues.Any() && !field.AllowedValues.Contains(value))
                {
                    result.Errors.Add(new EnvelopeValidationError
                    {
                        Field = field.FieldName,
                        ErrorCode = "INVALID_VALUE",
                        Message = $"Field '{field.DisplayName}' contains invalid value",
                        Severity = EnvelopeValidationSeverity.Major,
                        ActualValue = value,
                        ExpectedValue = string.Join(", ", field.AllowedValues),
                        SuggestedFix = $"Use one of: {string.Join(", ", field.AllowedValues)}"
                    });
                    result.Summary.MajorErrors++;
                }
            }
        }

        private async Task<EnvelopeRepairResult> AttemptGracefulRepair(string malformedResponse, EnvelopeSchema expectedSchema)
        {
            var result = new EnvelopeRepairResult
            {
                StrategyUsed = EnvelopeRepairStrategy.GracefulDegradation
            };

            // Try to extract JSON from response using common patterns
            var jsonPatterns = new[]
            {
                @"\{[\s\S]*\}",  // Basic JSON object
                @"```json\s*(\{[\s\S]*?\})\s*```",  // Code block
                @"```\s*(\{[\s\S]*?\})\s*```"  // Generic code block
            };

            foreach (var pattern in jsonPatterns)
            {
                var match = Regex.Match(malformedResponse, pattern, RegexOptions.Multiline);
                if (match.Success)
                {
                    var extractedJson = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                    
                    try
                    {
                        var envelope = await TryDirectParseAsync(extractedJson, expectedSchema);
                        if (envelope != null)
                        {
                            result.RepairSuccessful = true;
                            result.RepairedEnvelope = envelope;
                            result.RepairConfidence = 0.8;
                            result.ActionsPerformed.Add(new EnvelopeRepairAction
                            {
                                ActionType = "JSONExtracted",
                                Description = $"Extracted JSON using pattern: {pattern}",
                                OriginalValue = malformedResponse,
                                RepairedValue = extractedJson,
                                Confidence = 0.8
                            });
                            return result;
                        }
                    }
                    catch (JsonException)
                    {
                        continue;
                    }
                }
            }

            result.RepairSuccessful = false;
            result.FailureReason = "Could not extract valid JSON from response";
            return result;
        }

        private async Task<EnvelopeRepairResult> AttemptBestEffortRecovery(string malformedResponse, EnvelopeSchema expectedSchema)
        {
            // First try graceful repair
            var gracefulResult = await AttemptGracefulRepair(malformedResponse, expectedSchema);
            if (gracefulResult.RepairSuccessful)
            {
                gracefulResult.StrategyUsed = EnvelopeRepairStrategy.BestEffortRecovery;
                return gracefulResult;
            }

            // If graceful repair failed, try field extraction
            return await AttemptFieldExtraction(malformedResponse, expectedSchema);
        }

        private async Task<EnvelopeRepairResult> AttemptFieldExtraction(string malformedResponse, EnvelopeSchema expectedSchema)
        {
            var result = new EnvelopeRepairResult
            {
                StrategyUsed = EnvelopeRepairStrategy.BestEffortRecovery
            };

            var extractedData = new Dictionary<string, object>();
            
            // Try to extract required fields using field names
            foreach (var field in expectedSchema.RequiredFields)
            {
                var patterns = new[]
                {
                    $@"""{field.FieldName}""\s*:\s*""([^""]*)", // JSON string pattern
                    $@"{field.FieldName}:\s*(.+?)[\n\r]", // Free form pattern
                    $@"{field.DisplayName}:\s*(.+?)[\n\r]" // Display name pattern
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(malformedResponse, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    if (match.Success)
                    {
                        extractedData[field.FieldName] = match.Groups[1].Value.Trim();
                        result.ActionsPerformed.Add(new EnvelopeRepairAction
                        {
                            ActionType = "FieldExtracted",
                            Field = field.FieldName,
                            Description = $"Extracted field using pattern: {pattern}",
                            RepairedValue = match.Groups[1].Value.Trim(),
                            Confidence = 0.6
                        });
                        break;
                    }
                }

                // Apply default if field not found
                if (!extractedData.ContainsKey(field.FieldName) && !string.IsNullOrEmpty(field.DefaultValue))
                {
                    extractedData[field.FieldName] = field.DefaultValue;
                    result.ActionsPerformed.Add(new EnvelopeRepairAction
                    {
                        ActionType = "DefaultApplied",
                        Field = field.FieldName,
                        Description = "Applied default value for missing field",
                        RepairedValue = field.DefaultValue,
                        Confidence = 0.3
                    });
                }
            }

            if (extractedData.Any())
            {
                try
                {
                    var jsonString = JsonSerializer.Serialize(extractedData);
                    var envelope = await TryDirectParseAsync(jsonString, expectedSchema);
                    
                    if (envelope != null)
                    {
                        result.RepairSuccessful = true;
                        result.RepairedEnvelope = envelope;
                        result.RepairConfidence = Math.Min(1.0, 0.4 + (extractedData.Count * 0.1));
                        return result;
                    }
                }
                catch (JsonException ex)
                {
                    result.RepairWarnings.Add($"Field extraction created invalid JSON: {ex.Message}");
                }
            }

            result.RepairSuccessful = false;
            result.FailureReason = "Could not extract sufficient field data from response";
            return result;
        }

        private EnvelopeRepairResult CreateRawFallbackEnvelope(string malformedResponse, EnvelopeSchema expectedSchema)
        {
            var result = new EnvelopeRepairResult
            {
                StrategyUsed = EnvelopeRepairStrategy.FallbackToRaw,
                RepairSuccessful = true,
                RepairConfidence = 0.1 // Very low confidence for raw fallback
            };

            // Create envelope with raw response as a single field
            var fallbackData = new Dictionary<string, object>
            {
                ["rawResponse"] = malformedResponse,
                ["processingNote"] = "Original response could not be parsed into structured format"
            };

            try
            {
                var jsonString = JsonSerializer.Serialize(fallbackData);
                var jsonDoc = JsonDocument.Parse(jsonString);
                
                var envelope = CreateEnvelope(Guid.NewGuid().ToString(), EnvelopeType.ErrorResponse);
                envelope.StructuredData = jsonDoc;
                envelope.RawResponse = malformedResponse;
                envelope.CompletenessScore = 0.1;
                
                result.RepairedEnvelope = envelope;
                result.ActionsPerformed.Add(new EnvelopeRepairAction
                {
                    ActionType = "RawFallback",
                    Description = "Created fallback envelope with raw response data",
                    OriginalValue = malformedResponse,
                    RepairedValue = jsonString,
                    Confidence = 0.1
                });

                return result;
            }
            catch (Exception ex)
            {
                result.RepairSuccessful = false;
                result.FailureReason = $"Even fallback envelope creation failed: {ex.Message}";
                return result;
            }
        }
    }
}