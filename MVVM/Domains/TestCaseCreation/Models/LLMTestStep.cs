namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models
{
    /// <summary>
    /// Individual step in an LLM-generated test case execution sequence.
    /// </summary>
    public class LLMTestStep
    {
        public int StepNumber { get; set; }
        public string Action { get; set; } = string.Empty;
        public string ExpectedResult { get; set; } = string.Empty;
        public string TestData { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }
}
