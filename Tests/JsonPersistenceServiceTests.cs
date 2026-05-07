using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.Services;
using System.IO;

namespace TestCaseEditorApp.Tests
{
    [TestClass]
    public class JsonPersistenceServiceTests
    {
        private JsonPersistenceService _service;
        private string _testDirectory;

        [TestInitialize]
        public void Setup()
        {
            _service = new JsonPersistenceService();
            _testDirectory = Path.Combine(Path.GetTempPath(), "TestCaseEditorApp_Tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        [TestMethod]
        public void Save_WithValidObject_ShouldPersistData()
        {
            // Arrange
            var testData = new TestClass { Id = Guid.NewGuid(), Name = "Test", Count = 42 };
            var key = "test-object";

            // Act
            _service.Save(key, testData);

            // Assert
            Assert.IsTrue(_service.Exists(key));
        }

        [TestMethod]
        public void Load_WithExistingKey_ShouldReturnObject()
        {
            // Arrange
            var testData = new TestClass { Id = Guid.NewGuid(), Name = "Test", Count = 42 };
            var key = "test-load";
            _service.Save(key, testData);

            // Act
            var result = _service.Load<TestClass>(key);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(testData.Id, result.Id);
            Assert.AreEqual(testData.Name, result.Name);
            Assert.AreEqual(testData.Count, result.Count);
        }

        [TestMethod]
        public void Load_WithNonExistentKey_ShouldReturnDefault()
        {
            // Act
            var result = _service.Load<TestClass>("nonexistent-key");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void Exists_WithExistingKey_ShouldReturnTrue()
        {
            // Arrange
            var testData = new { Name = "Test" };
            var key = "exists-test";
            _service.Save(key, testData);

            // Act
            var result = _service.Exists(key);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Exists_WithNonExistentKey_ShouldReturnFalse()
        {
            // Act
            var result = _service.Exists("non-existent-key");

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Save_WithAbsolutePath_ShouldCreateFileAtPath()
        {
            // Arrange
            var testData = new { Name = "Absolute Path Test", Value = 123 };
            var absolutePath = Path.Combine(_testDirectory, "absolute-test.json");

            // Act
            _service.Save(absolutePath, testData);

            // Assert
            Assert.IsTrue(File.Exists(absolutePath));
            
            // Verify content by loading directly from filesystem
            var content = File.ReadAllText(absolutePath);
            Assert.IsTrue(content.Contains("Absolute Path Test"));
            Assert.IsTrue(content.Contains("123"));
        }

        [TestMethod]
        public void SaveLoadRoundTrip_ShouldPreserveData()
        {
            // Arrange
            var original = new TestClass 
            { 
                Id = Guid.NewGuid(), 
                Name = "Round Trip Test", 
                Count = 987 
            };
            var key = "roundtrip-test";

            // Act
            _service.Save(key, original);
            var loaded = _service.Load<TestClass>(key);

            // Assert
            Assert.IsNotNull(loaded);
            Assert.AreEqual(original.Id, loaded.Id);
            Assert.AreEqual(original.Name, loaded.Name);
            Assert.AreEqual(original.Count, loaded.Count);
        }

        private class TestClass
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Count { get; set; }
        }
    }
}