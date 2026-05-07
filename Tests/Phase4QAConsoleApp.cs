using System;
using System.Threading.Tasks;
using TestCaseEditorApp.Tests.Integration;

namespace TestCaseEditorApp.Tests
{
    /// <summary>
    /// Console application to run Phase 4 Integration Quality Assurance tests
    /// </summary>
    class Phase4QAConsoleApp
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("🎯 PHASE 4 INTEGRATION QUALITY ASSURANCE TEST RUNNER");
            Console.WriteLine("====================================================");
            Console.WriteLine();

            try
            {
                var testResult = await Phase4IntegrationQualityAssurance.RunCompleteQualityAssurance();
                
                if (testResult)
                {
                    Console.WriteLine();
                    Console.WriteLine("🎉 ALL TESTS PASSED - Phase 4 integration is ready for production!");
                    return 0; // Success exit code
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("💥 SOME TESTS FAILED - Review issues before proceeding to Phase 5");
                    return 1; // Failure exit code
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ CRITICAL ERROR during test execution: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                return 2; // Critical error exit code
            }
        }
    }
}