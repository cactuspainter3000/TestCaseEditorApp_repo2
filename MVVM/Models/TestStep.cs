// TestStep.cs
using System.Collections.Generic;

namespace TestCaseEditorApp.MVVM.Models
{
    public class TestStep
    {
        public int StepNumber { get; set; }
        public string StepAction { get; set; } = string.Empty;
        public string StepExpectedResult { get; set; } = string.Empty;
        public string StepNotes { get; set; } = string.Empty;
    }
}


