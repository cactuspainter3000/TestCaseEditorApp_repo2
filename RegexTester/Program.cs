using System;
using System.Text.RegularExpressions;

namespace RegexTester
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Jama Requirement Header Regex Tester");
            Console.WriteLine("=====================================");
            Console.WriteLine();

            // The regex pattern from the document
            string pattern = @"^(?:(?<lead>\d+(?:\.\d+)*?)\s+)?(?<item>[A-Z0-9_-]+-REQ_RC-\d+)\s+(?:(?<heading>\d+(?:\.\d+)*?)\s+)?(?<name>.+?)$";
            
            Console.WriteLine("Regex Pattern:");
            Console.WriteLine(pattern);
            Console.WriteLine();

            // Test samples from the document
            string[] testSamples = {
                "DECAGON-REQ_RC-11 *Safety Checklist Completion*",
                "DECAGON-REQ_RC-16 Test Data Archiving",
                "2.1 DECAGON-REQ_RC-11 *Safety Checklist Completion*",
                "DECAGON-REQ_RC-19 Connector/Cable Mating Protection"
            };

            Regex regex = new Regex(pattern, RegexOptions.Compiled);

            for (int i = 0; i < testSamples.Length; i++)
            {
                Console.WriteLine($"Test {i + 1}: \"{testSamples[i]}\"");
                Console.WriteLine(new string('-', 50));

                Match match = regex.Match(testSamples[i]);
                
                if (match.Success)
                {
                    Console.WriteLine("✅ MATCH!");
                    Console.WriteLine("Captured Groups:");
                    
                    // Show all named groups
                    foreach (string groupName in regex.GetGroupNames())
                    {
                        if (groupName != "0") // Skip the full match group
                        {
                            Group group = match.Groups[groupName];
                            if (group.Success)
                            {
                                Console.WriteLine($"  {groupName}: \"{group.Value}\"");
                            }
                            else
                            {
                                Console.WriteLine($"  {groupName}: (no match)");
                            }
                        }
                    }
                    
                    Console.WriteLine($"  Full Match: \"{match.Value}\"");
                }
                else
                {
                    Console.WriteLine("❌ NO MATCH");
                    
                    // Try to provide some debugging info
                    Console.WriteLine("Debugging analysis:");
                    
                    // Check if it contains the required item pattern
                    string itemPattern = @"[A-Z0-9_-]+-REQ_RC-\d+";
                    Match itemMatch = Regex.Match(testSamples[i], itemPattern);
                    if (itemMatch.Success)
                    {
                        Console.WriteLine($"  ✓ Found item pattern: \"{itemMatch.Value}\"");
                    }
                    else
                    {
                        Console.WriteLine("  ✗ Item pattern not found");
                    }
                }
                
                Console.WriteLine();
            }

            // Additional analysis - let's break down the regex components
            Console.WriteLine("Regex Component Analysis:");
            Console.WriteLine("========================");
            Console.WriteLine("(?:(?<lead>\\d+(?:\\.\\d+)*?)\\s+)?     - Optional leading number (e.g., '2.1 ')");
            Console.WriteLine("(?<item>[A-Z0-9_-]+-REQ_RC-\\d+)      - Required item ID (e.g., 'DECAGON-REQ_RC-11')");
            Console.WriteLine("\\s+                                   - Required whitespace");
            Console.WriteLine("(?:(?<heading>\\d+(?:\\.\\d+)*?)\\s+)? - Optional heading number");
            Console.WriteLine("(?<name>.+?)                          - Requirement name (everything else)");
            Console.WriteLine();

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}