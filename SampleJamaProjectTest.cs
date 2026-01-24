using System;
using System.IO;
using System.Text.Json;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp
{
    /// <summary>
    /// Simple test to validate sample Jama JSON structure
    /// </summary>
    public class SampleJamaProjectTest
    {
        public static void TestJsonStructure()
        {
            try
            {
                var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "sample-jama-project.json");
                Console.WriteLine($"Testing JSON file: {jsonPath}");
                
                if (!File.Exists(jsonPath))
                {
                    Console.WriteLine("❌ Sample JSON file not found!");
                    return;
                }
                
                var jsonContent = File.ReadAllText(jsonPath);
                Console.WriteLine($"✅ JSON file loaded. Size: {jsonContent.Length} characters");
                
                // Test JSON parsing
                var jsonDocument = JsonDocument.Parse(jsonContent);
                Console.WriteLine("✅ JSON is valid and parseable");
                
                // Check structure
                if (jsonDocument.RootElement.TryGetProperty("workspace", out var workspace))
                {
                    Console.WriteLine("✅ 'workspace' property found");
                    
                    if (workspace.TryGetProperty("requirements", out var requirements))
                    {
                        var reqCount = requirements.GetArrayLength();
                        Console.WriteLine($"✅ Requirements array found with {reqCount} items");
                        
                        // Check first requirement structure
                        if (reqCount > 0)
                        {
                            var firstReq = requirements[0];
                            var hasItem = firstReq.TryGetProperty("item", out var item);
                            var hasName = firstReq.TryGetProperty("name", out var name);
                            var hasMethod = firstReq.TryGetProperty("method", out var method);
                            
                            Console.WriteLine($"✅ First requirement structure:");
                            Console.WriteLine($"   - Item: {(hasItem ? item.GetString() : "MISSING")}");
                            Console.WriteLine($"   - Name: {(hasName ? name.GetString() : "MISSING")}");
                            Console.WriteLine($"   - Method: {(hasMethod ? method.GetString() : "MISSING")}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("❌ 'requirements' array not found in workspace");
                    }
                }
                else
                {
                    Console.WriteLine("❌ 'workspace' property not found");
                }
                
                Console.WriteLine("\n🎯 Expected behavior when imported:");
                Console.WriteLine("   - Notification area should show: '8 requirements | 0% analyzed | 0% with test cases'");
                Console.WriteLine("   - Method should show: 'Test' (from first requirement TSP-REQ-001)");
                Console.WriteLine("   - Title should show: 'Test_Sample_Project'");
                Console.WriteLine("   - Requirements tab should show all 8 requirements");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error testing JSON: {ex.Message}");
            }
        }
    }
}