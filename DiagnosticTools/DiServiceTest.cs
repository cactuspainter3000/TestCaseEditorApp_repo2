using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels;

namespace TestCaseEditorApp.DiagnosticTools
{
    public static class DiServiceTest
    {
        public static void TestNewProjectViewModelResolution()
        {
            try
            {
                Console.WriteLine("=== DI Resolution Test ===");
                Console.WriteLine("Attempting to resolve NewProjectWorkflowViewModel...");
                
                var serviceProvider = TestCaseEditorApp.App.ServiceProvider;
                if (serviceProvider == null)
                {
                    Console.WriteLine("ERROR: ServiceProvider is null!");
                    return;
                }

                Console.WriteLine("ServiceProvider exists. Attempting GetService...");
                
                // Try to resolve the ViewModel
                var viewModel = serviceProvider.GetService<NewProjectWorkflowViewModel>();
                
                if (viewModel != null)
                {
                    Console.WriteLine("SUCCESS: NewProjectWorkflowViewModel resolved successfully!");
                    Console.WriteLine($"Type: {viewModel.GetType().FullName}");
                }
                else
                {
                    Console.WriteLine("FAILED: NewProjectWorkflowViewModel resolution returned null");
                    
                    // Test individual dependencies
                    Console.WriteLine("\nTesting individual dependencies:");
                    
                    var mediator = serviceProvider.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.Mediators.INewProjectMediator>();
                    Console.WriteLine($"INewProjectMediator: {(mediator != null ? "OK" : "FAILED")}");
                    
                    var logger = serviceProvider.GetService<Microsoft.Extensions.Logging.ILogger<NewProjectWorkflowViewModel>>();
                    Console.WriteLine($"ILogger<NewProjectWorkflowViewModel>: {(logger != null ? "OK" : "FAILED")}");
                    
                    var anythingLLM = serviceProvider.GetService<TestCaseEditorApp.Services.AnythingLLMService>();
                    Console.WriteLine($"AnythingLLMService: {(anythingLLM != null ? "OK" : "FAILED")}");
                    
                    var toastService = serviceProvider.GetService<TestCaseEditorApp.Services.ToastNotificationService>();
                    Console.WriteLine($"ToastNotificationService: {(toastService != null ? "OK" : "FAILED")}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EXCEPTION during DI resolution test: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }
    }
}