using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;

namespace TestCaseEditorApp.Testing
{
    public static class PicklistDebugTester
    {
        public static async Task TestPicklistResolution()
        {
            // Test the specific picklist IDs from the user's screenshot
            var testIds = new[] { "1614", "1649", "43408", "1658" };
            
            Console.WriteLine("Testing Jama picklist resolution...");
            
            foreach (var id in testIds)
            {
                Console.WriteLine($"\nTesting ID: {id}");
                
                try
                {
                    // Test direct API call
                    var httpClient = new HttpClient();
                    var baseUrl = "https://jama02.rockwellcollins.com/contour";
                    var url = $"{baseUrl}/rest/v1/picklistoptions/{id}";
                    
                    Console.WriteLine($"URL: {url}");
                    
                    var response = await httpClient.GetAsync(url);
                    Console.WriteLine($"Status: {response.StatusCode}");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Response: {json}");
                        
                        var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("data", out var dataElement) &&
                            dataElement.TryGetProperty("name", out var nameElement))
                        {
                            var name = nameElement.GetString();
                            Console.WriteLine($"Resolved: {id} -> {name}");
                        }
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Error: {errorContent}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception: {ex.Message}");
                }
            }
        }
    }
}