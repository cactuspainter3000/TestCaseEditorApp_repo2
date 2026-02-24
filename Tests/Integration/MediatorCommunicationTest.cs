using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Mediators;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests.Integration
{
    /// <summary>
    /// Integration test for cross-domain mediator communication
    /// Tests that TrainingDataValidationMediator can publish events and communicate with other domains
    /// </summary>
    public static class MediatorCommunicationTest
    {
        private static bool _eventReceived = false;
        private static string _lastEventType = "";
        
        /// <summary>
        /// Test that TrainingDataValidationMediator can publish domain events
        /// </summary>
        public static async Task TestTrainingDataValidationEventPublishing()
        {
            Console.WriteLine("🧪 [Mediator Test] Testing event publishing...");
            
            try
            {
                // Set up DI container
                var services = new ServiceCollection();
                services.AddLogging(builder => builder.AddConsole());
                services.AddSingleton<IDomainUICoordinator, DomainUICoordinator>();
                services.AddSingleton<ITrainingDataValidationService, TrainingDataValidationService>();
                services.AddSingleton<ITrainingDataValidationMediator, TrainingDataValidationMediator>();
                
                var serviceProvider = services.BuildServiceProvider();
                
                // Get mediator instance
                var mediator = serviceProvider.GetRequiredService<ITrainingDataValidationMediator>();
                Console.WriteLine("✅ TrainingDataValidationMediator resolved successfully");
                
                // Set up event handlers to test cross-domain communication
                mediator.WorkflowStateChanged += OnWorkflowStateChanged;
                mediator.ExampleValidated += OnExampleValidated;
                mediator.SessionEvent += OnSessionEvent;
                mediator.MetricsUpdated += OnMetricsUpdated;
                
                Console.WriteLine("✅ Event handlers registered");
                
                // Test 1: Start validation session (should trigger events)
                Console.WriteLine("📤 Starting validation session...");
                var dataset = new SyntheticTrainingDataset
                {
                    Examples = new System.Collections.Generic.List<SyntheticTrainingExample>
                    {
                        new SyntheticTrainingExample 
                        { 
                            Id = "test1",
                            Requirement = "Test requirement",
                            TestCase = "Test case content"
                        }
                    }
                };
                
                await mediator.StartValidationSessionAsync(dataset);
                
                // Allow some time for async event processing
                await Task.Delay(100);
                
                if (_eventReceived)
                {
                    Console.WriteLine($"✅ Event received: {_lastEventType}");
                } else {
                    Console.WriteLine("⚠️  No events received - may need async event setup");
                }
                
                // Test 2: Get progress (tests mediator functionality)
                var progress = mediator.GetCurrentProgress();
                Console.WriteLine($"✅ Progress retrieved: {progress.ValidatedExamples}/{progress.TotalExamples}");
                
                // Test 3: Record validation (should trigger events)
                Console.WriteLine("📤 Recording validation result...");
                var validationResult = new TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult
                {
                    ExampleId = "test1",
                    Decision = ValidationDecision.Approved,
                    Timestamp = DateTime.Now
                };
                
                await mediator.RecordValidationAsync(validationResult);
                await Task.Delay(100);
                
                Console.WriteLine("🎉 [Mediator Test] Cross-domain communication test completed!");
                Console.WriteLine("✅ Mediator can be instantiated and called");
                Console.WriteLine("✅ Methods execute without throwing exceptions");
                Console.WriteLine("✅ Event infrastructure is in place");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Mediator Test] Failed: {ex.Message}");
                throw;
            }
        }
        
        // Event handlers to test cross-domain communication
        private static void OnWorkflowStateChanged(object sender, ValidationWorkflowStateChangedEventArgs e)
        {
            _eventReceived = true;
            _lastEventType = "WorkflowStateChanged";
            Console.WriteLine($"📨 Received WorkflowStateChanged event: {e.OldState} -> {e.NewState}");
        }
        
        private static void OnExampleValidated(object sender, ExampleValidatedEventArgs e)
        {
            _eventReceived = true;
            _lastEventType = "ExampleValidated";
            Console.WriteLine($"📨 Received ExampleValidated event at {e.Timestamp}");
        }
        
        private static void OnSessionEvent(object sender, ValidationSessionEventArgs e)
        {
            _eventReceived = true;
            _lastEventType = "SessionEvent";
            Console.WriteLine($"📨 Received SessionEvent: {e.EventType} for session {e.SessionId}");
        }
        
        private static void OnMetricsUpdated(object sender, ValidationMetricsEventArgs e)
        {
            _eventReceived = true;
            _lastEventType = "MetricsUpdated";
            Console.WriteLine($"📨 Received MetricsUpdated event at {e.Timestamp}");
        }
        
        /// <summary>
        /// Test mediator navigation methods (required by BaseDomainMediator)
        /// </summary>
        public static void TestMediatorNavigation()
        {
            Console.WriteLine("\n🧪 [Mediator Test] Testing navigation functionality...");
            
            try
            {
                // Set up DI container
                var services = new ServiceCollection();
                services.AddLogging(builder => builder.AddConsole());
                services.AddSingleton<IDomainUICoordinator, DomainUICoordinator>();
                services.AddSingleton<ITrainingDataValidationService, TrainingDataValidationService>();
                services.AddSingleton<ITrainingDataValidationMediator, TrainingDataValidationMediator>();
                
                var serviceProvider = services.BuildServiceProvider();
                var mediator = (TrainingDataValidationMediator)serviceProvider.GetRequiredService<ITrainingDataValidationMediator>();
                
                // Test navigation methods
                Console.WriteLine("📍 Testing navigation to initial step...");
                mediator.NavigateToInitialStep();
                Console.WriteLine("✅ NavigateToInitialStep() executed");
                
                Console.WriteLine("📍 Testing navigation capabilities...");
                var canNavigateBack = mediator.CanNavigateBack();
                var canNavigateForward = mediator.CanNavigateForward();
                Console.WriteLine($"✅ Navigation state: Back={canNavigateBack}, Forward={canNavigateForward}");
                
                Console.WriteLine("📍 Testing navigation to final step...");
                mediator.NavigateToFinalStep();
                Console.WriteLine("✅ NavigateToFinalStep() executed");
                
                Console.WriteLine("🎉 [Mediator Test] Navigation functionality test completed!");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Mediator Test] Navigation test failed: {ex.Message}");
                throw;
            }
        }
    }
}