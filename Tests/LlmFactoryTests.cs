using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.Tests
{
    [TestClass]
    public class LlmFactoryTests
    {
        [TestMethod]
        public void CreateLazy_ShouldReturnLazyService()
        {
            // Act
            var lazyService = LlmFactory.CreateLazy();

            // Assert
            Assert.IsNotNull(lazyService);
            Assert.IsInstanceOfType(lazyService, typeof(ITextGenerationService));
        }

        [TestMethod]
        public void CreateLazy_ShouldDeferInitialization()
        {
            // Arrange - request ollama but with skip validation
            Environment.SetEnvironmentVariable("SKIP_LLM_VALIDATION", "true");

            try
            {
                // Act - this should not throw even though ollama may not be available
                var lazyService = LlmFactory.CreateLazy();

                // Assert
                Assert.IsNotNull(lazyService);
                // The service is created but not initialized until first use
            }
            finally
            {
                Environment.SetEnvironmentVariable("SKIP_LLM_VALIDATION", null);
            }
        }

        [TestMethod]
        public async Task LazyService_ShouldInitializeOnFirstCall()
        {
            // Arrange
            var lazyService = LlmFactory.CreateLazy();

            // Act - first call should initialize the inner service
            var result = await lazyService.GenerateAsync("test prompt");

            // Assert
            Assert.IsNotNull(result);
            // NoopTextGenerationService should return a default response
        }

        [TestMethod]
        public void Create_WithNoopProvider_ShouldReturnNoopService()
        {
            // Arrange
            Environment.SetEnvironmentVariable("LLM_PROVIDER", "NoOp");
            
            // Act
            var service = LlmFactory.Create();

            // Assert  
            Assert.IsNotNull(service);
            // Should be able to create noop service immediately without validation
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Create_WithOllamaAndNoSkipValidation_ShouldThrowWhenOllamaUnavailable()
        {
            // Arrange - ensure skip validation is not set
            Environment.SetEnvironmentVariable("SKIP_LLM_VALIDATION", null);
            Environment.SetEnvironmentVariable("LLM_PROVIDER", "Ollama");

            // Act - this should throw since ollama is likely not running in test environment
            LlmFactory.Create();

            // Assert - ExpectedException attribute handles this
        }
    }
}