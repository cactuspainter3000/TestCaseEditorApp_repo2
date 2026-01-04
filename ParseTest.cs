using System;
using System.IO;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;

namespace TestCaseEditorApp
{
    /// <summary>
    /// Simple test program to debug requirement parsing
    /// </summary>
    public class ParseTest
    {
        public static void Main(string[] args)
        {
            string docPath = @"C:\Users\e10653214\Downloads\Decagon New Edition.docx";
            
            if (!File.Exists(docPath))
            {
                Console.WriteLine($"Document not found at {docPath}");
                return;
            }

            Console.WriteLine("Testing Jama All Data DOCX Parser...");
            Console.WriteLine($"Document: {docPath}");
            
            try
            {
                var requirements = JamaAllDataDocxParser.Parse(docPath, debugDump: true);
                
                Console.WriteLine($"✅ Successfully parsed {requirements.Count} requirements");
                
                for (int i = 0; i < Math.Min(3, requirements.Count); i++)
                {
                    var req = requirements[i];
                    Console.WriteLine($"\n--- Requirement {i + 1} ---");
                    Console.WriteLine($"Item: {req.Item}");
                    Console.WriteLine($"Name: {req.Name}");
                    Console.WriteLine($"Description: {req.Description?.Substring(0, Math.Min(100, req.Description.Length))}...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error parsing document: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}