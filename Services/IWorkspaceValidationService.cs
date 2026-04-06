using System.Text.Json;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for validating workspace data before save operations
    /// </summary>
    public interface IWorkspaceValidationService
    {
        ValidationResult ValidateWorkspace(Workspace workspace);
        ValidationResult ValidateRequirements(ICollection<Requirement> requirements);
        ValidationResult ValidateJsonStructure(string json, Type expectedType);
    }

    public class WorkspaceValidationService : IWorkspaceValidationService
    {
        public ValidationResult ValidateWorkspace(Workspace workspace)
        {
            if (workspace == null)
                return ValidationResult.Error("Workspace cannot be null");

            if (string.IsNullOrWhiteSpace(workspace.Name))
                return ValidationResult.Error("Workspace name is required");

            if (workspace.Version <= 0)
                return ValidationResult.Error("Invalid workspace version");

            // Validate requirements collection
            var requirementsValidation = ValidateRequirements(workspace.Requirements);
            if (!requirementsValidation.IsValid)
                return requirementsValidation;

            // Validate that we can serialize the workspace
            try
            {
                var json = JsonSerializer.Serialize(workspace);
                if (string.IsNullOrWhiteSpace(json))
                    return ValidationResult.Error("Failed to serialize workspace to JSON");
            }
            catch (Exception ex)
            {
                return ValidationResult.Error($"JSON serialization failed: {ex.Message}");
            }

            return ValidationResult.Success();
        }

        public ValidationResult ValidateRequirements(ICollection<Requirement> requirements)
        {
            if (requirements == null)
                return ValidationResult.Error("Requirements collection cannot be null");

            var duplicateItems = requirements
                .Where(r => !string.IsNullOrWhiteSpace(r.Item))
                .GroupBy(r => r.Item)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateItems.Any())
            {
                return ValidationResult.Error($"Duplicate requirement IDs found: {string.Join(", ", duplicateItems)}");
            }

            var invalidRequirements = requirements
                .Where(r => string.IsNullOrWhiteSpace(r.Item) || string.IsNullOrWhiteSpace(r.Name))
                .Count();

            if (invalidRequirements > 0)
            {
                return ValidationResult.Warning($"{invalidRequirements} requirements have missing ID or Name");
            }

            return ValidationResult.Success();
        }

        public ValidationResult ValidateJsonStructure(string json, Type expectedType)
        {
            if (string.IsNullOrWhiteSpace(json))
                return ValidationResult.Error("JSON content is empty");

            try
            {
                var obj = JsonSerializer.Deserialize(json, expectedType);
                if (obj == null)
                    return ValidationResult.Error("Deserialized object is null");

                return ValidationResult.Success();
            }
            catch (JsonException ex)
            {
                return ValidationResult.Error($"Invalid JSON structure: {ex.Message}");
            }
            catch (Exception ex)
            {
                return ValidationResult.Error($"Validation error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Result of workspace validation operations
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; private set; }
        public ValidationSeverity Severity { get; private set; }
        public string ErrorMessage { get; private set; } = string.Empty;
        public List<string> Details { get; private set; } = new();

        private ValidationResult(bool isValid, ValidationSeverity severity, string message)
        {
            IsValid = isValid;
            Severity = severity;
            ErrorMessage = message;
        }

        public static ValidationResult Success() 
            => new(true, ValidationSeverity.None, string.Empty);

        public static ValidationResult Warning(string message) 
            => new(true, ValidationSeverity.Warning, message);

        public static ValidationResult Error(string message) 
            => new(false, ValidationSeverity.Error, message);

        public ValidationResult WithDetails(params string[] details)
        {
            Details.AddRange(details);
            return this;
        }
    }

    public enum ValidationSeverity
    {
        None,
        Warning, 
        Error
    }
}