using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Mediators;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests.Integration
{
    /// <summary>
    /// Integration test to verify DI container setup for TrainingDataValidation domain
    /// Tests that all services can be resolved and their dependencies are satisfied
    /// </summary>
    public class DI_IntegrationTest
    {
        /// <summary>
        /// Test that verifies all TrainingDataValidation services can be resolved from DI container
        /// This validates our architectural compliance fixes and proper registration
        /// </summary>
        public static void TestTrainingDataValidationDIResolution()
        {
            try
            {
                Console.WriteLine("🧪 [DI Integration Test] Starting TrainingDataValidation service resolution test...");
                
                // Create a minimal service collection similar to App.xaml.cs
                var services = new ServiceCollection();
                
                // Add logging (required by mediators)
                services.AddLogging(builder => builder.AddConsole());
                
                // Add UI coordinator (required by BaseDomainMediator)
                services.AddSingleton<IDomainUICoordinator, DomainUICoordinator>();
                
                // Add synthetic data generator (required by TrainingDataValidationViewModel)
                services.AddSingleton<ISyntheticTrainingDataGenerator, SyntheticTrainingDataGenerator>();
                
                // Add TrainingDataValidation services
                services.AddSingleton<ITrainingDataValidationService, TrainingDataValidationService>();
                services.AddSingleton<ITrainingDataValidationMediator, TrainingDataValidationMediator>();
                services.AddTransient<TrainingDataValidationViewModel>();
                
                // Build service provider
                var serviceProvider = services.BuildServiceProvider();
                Console.WriteLine("✅ Service provider built successfully");
                
                // Test 1: Resolve TrainingDataValidationService
                var validationService = serviceProvider.GetService<ITrainingDataValidationService>();
                if (validationService == null)
                    throw new Exception("❌ Failed to resolve ITrainingDataValidationService");
                Console.WriteLine("✅ ITrainingDataValidationService resolved successfully");
                
                // Test 2: Resolve TrainingDataValidationMediator  
                var mediator = serviceProvider.GetService<ITrainingDataValidationMediator>();
                if (mediator == null)
                    throw new Exception("❌ Failed to resolve ITrainingDataValidationMediator");
                Console.WriteLine("✅ ITrainingDataValidationMediator resolved successfully");
                
                // Test 3: Resolve TrainingDataValidationViewModel
                var viewModel = serviceProvider.GetService<TrainingDataValidationViewModel>();
                if (viewModel == null)
                    throw new Exception("❌ Failed to resolve TrainingDataValidationViewModel");
                Console.WriteLine("✅ TrainingDataValidationViewModel resolved successfully");
                
                // Test 4: Verify ViewModel can be instantiated multiple times (Transient)
                var viewModel2 = serviceProvider.GetService<TrainingDataValidationViewModel>();
                if (viewModel2 == null || ReferenceEquals(viewModel, viewModel2))
                    throw new Exception("❌ TrainingDataValidationViewModel should be transient (different instances)");
                Console.WriteLine("✅ TrainingDataValidationViewModel is properly transient");
                
                // Test 5: Verify Mediator is singleton
                var mediator2 = serviceProvider.GetService<ITrainingDataValidationMediator>();
                if (mediator2 == null || !ReferenceEquals(mediator, mediator2))
                    throw new Exception("❌ ITrainingDataValidationMediator should be singleton (same instance)");
                Console.WriteLine("✅ ITrainingDataValidationMediator is properly singleton");
                
                Console.WriteLine("🎉 [DI Integration Test] All TrainingDataValidation services resolved successfully!");
                Console.WriteLine($"   - Service: {validationService.GetType().Name}");
                Console.WriteLine($"   - Mediator: {mediator.GetType().Name}");
                Console.WriteLine($"   - ViewModel: {viewModel.GetType().Name}");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [DI Integration Test] Failed: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Test basic functionality of resolved services to ensure they're working correctly
        /// </summary>
        public static void TestTrainingDataValidationBasicFunctionality()
        {
            try
            {
                Console.WriteLine("\n🧪 [DI Integration Test] Testing basic service functionality...");
                
                var services = new ServiceCollection();
                services.AddLogging(builder => builder.AddConsole());
                services.AddSingleton<IDomainUICoordinator, DomainUICoordinator>();
                services.AddSingleton<ISyntheticTrainingDataGenerator, SyntheticTrainingDataGenerator>();
                services.AddSingleton<ITrainingDataValidationService, TrainingDataValidationService>();
                services.AddSingleton<ITrainingDataValidationMediator, TrainingDataValidationMediator>();
                services.AddTransient<TrainingDataValidationViewModel>();
                
                var serviceProvider = services.BuildServiceProvider();
                
                // Test ViewModel initialization
                var viewModel = serviceProvider.GetRequiredService<TrainingDataValidationViewModel>();
                
                // Test that ViewModel properties are accessible
                var statusMessage = viewModel.StatusMessage;
                var isLoading = viewModel.IsLoading;
                var currentState = viewModel.CurrentState;
                
                Console.WriteLine($"✅ ViewModel initialized - Status: '{statusMessage}', Loading: {isLoading}, State: {currentState}");
                
                // Test that Mediator can be used for progress tracking
                var mediator = serviceProvider.GetRequiredService<ITrainingDataValidationMediator>();
                var progress = mediator.GetCurrentProgress();
                
                Console.WriteLine($"✅ Mediator functional - Progress: {progress.ValidatedExamples}/{progress.TotalExamples}");
                
                Console.WriteLine("🎉 [DI Integration Test] Basic functionality test passed!");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [DI Integration Test] Basic functionality test failed: {ex.Message}");
                throw;
            }
        }
    }
}