using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using System.Reflection;

namespace TestCaseEditorApp.Tests
{
    [TestClass]
    public class WorkspaceValidationServiceTests
    {
        private WorkspaceValidationService _validationService;

        [TestInitialize]
        public void Setup()
        {
            _validationService = new WorkspaceValidationService();
        }

        [TestMethod]
        public void ValidateWorkspace_WithValidWorkspace_ShouldReturnSuccess()
        {
            // Arrange
            var workspace = CreateWorkspaceWithName("Test Workspace");
            workspace.Version = 1;
            workspace.Requirements = new List<Requirement>();

            // Act
            var result = _validationService.ValidateWorkspace(workspace);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(ValidationSeverity.None, result.Severity);
        }

        [TestMethod]
        public void ValidateWorkspace_WithNullWorkspace_ShouldReturnError()
        {
            // Act
            var result = _validationService.ValidateWorkspace(null);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ValidationSeverity.Error, result.Severity);
            Assert.IsTrue(result.ErrorMessage.Contains("cannot be null"));
        }

        [TestMethod]
        public void ValidateWorkspace_WithEmptyName_ShouldReturnError()
        {
            // Arrange
            var workspace = CreateWorkspaceWithName("");
            workspace.Version = 1;
            workspace.Requirements = new List<Requirement>();

            // Act
            var result = _validationService.ValidateWorkspace(workspace);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ValidationSeverity.Error, result.Severity);
            Assert.IsTrue(result.ErrorMessage.Contains("name is required"));
        }

        [TestMethod]
        public void ValidateWorkspace_WithInvalidVersion_ShouldReturnError()
        {
            // Arrange
            var workspace = CreateWorkspaceWithName("Test Workspace");
            workspace.Version = 0;
            workspace.Requirements = new List<Requirement>();

            // Act
            var result = _validationService.ValidateWorkspace(workspace);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ValidationSeverity.Error, result.Severity);
            Assert.IsTrue(result.ErrorMessage.Contains("Invalid workspace version"));
        }

        [TestMethod]
        public void ValidateRequirements_WithValidRequirements_ShouldReturnSuccess()
        {
            // Arrange
            var requirements = new List<Requirement>
            {
                new Requirement { Item = "REQ-001", Name = "Test Requirement 1" },
                new Requirement { Item = "REQ-002", Name = "Test Requirement 2" }
            };

            // Act
            var result = _validationService.ValidateRequirements(requirements);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(ValidationSeverity.None, result.Severity);
        }

        [TestMethod]
        public void ValidateRequirements_WithNullRequirements_ShouldReturnError()
        {
            // Act
            var result = _validationService.ValidateRequirements(null);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ValidationSeverity.Error, result.Severity);
            Assert.IsTrue(result.ErrorMessage.Contains("cannot be null"));
        }

        [TestMethod]
        public void ValidateJsonStructure_WithValidJson_ShouldReturnSuccess()
        {
            // Arrange
            var json = "{\"Name\":\"Test\",\"Version\":1}";

            // Act
            var result = _validationService.ValidateJsonStructure(json, typeof(object));

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(ValidationSeverity.None, result.Severity);
        }

        [TestMethod]
        public void ValidateJsonStructure_WithInvalidJson_ShouldReturnError()
        {
            // Arrange
            var json = "{invalid json}";

            // Act
            var result = _validationService.ValidateJsonStructure(json, typeof(object));

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ValidationSeverity.Error, result.Severity);
            Assert.IsTrue(result.ErrorMessage.Contains("Invalid JSON structure"));
        }

        /// <summary>
        /// Helper method to create a workspace with a name using reflection to bypass the internal setter
        /// </summary>
        private Workspace CreateWorkspaceWithName(string name)
        {
            var workspace = new Workspace();
            var nameProperty = typeof(Workspace).GetProperty("Name");
            nameProperty?.SetValue(workspace, name);
            return workspace;
        }
    }
}