namespace TestCaseEditorApp.MVVM.Models
{
    // Use Jama’s definitive list (spelling kept as “Judgement”, label will map 1:1)
    public enum ValidationMethod
    {
        Unassigned,
        Analysis,
        EngineeringJudgement,
        Modeling,
        Similarity,
        Test,
        Traceability,
        Review,
        NotApplicable   // maps from "N/A"
    }
}
