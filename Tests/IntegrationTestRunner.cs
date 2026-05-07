using System;
using TestCaseEditorApp.Tests.Integration;

namespace TestCaseEditorApp.Tests
{
    /// <summary>
    /// Console test runner for integration tests
    /// Validates TrainingDataValidation domain integration after architectural compliance fixes
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("🚀 TrainingDataValidation Integration Test Suite");
            Console.WriteLine("================================================");
            
            try
            {
                // Test 1: DI Container Integration
                DI_IntegrationTest.TestTrainingDataValidationDIResolution();
                DI_IntegrationTest.TestTrainingDataValidationBasicFunctionality();
                
                Console.WriteLine("\n🎉 ALL INTEGRATION TESTS PASSED!");
                Console.WriteLine("✅ TrainingDataValidation domain is ready for full integration");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ INTEGRATION TEST FAILED: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                Environment.Exit(1);
            }
            
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }
    }
}