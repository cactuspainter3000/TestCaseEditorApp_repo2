using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests;

[TestClass]
public class JamaDocumentParserContextSanitizationTests
{
    private static string CallSanitizeRetrievedContext(string? context)
    {
        var method = typeof(JamaDocumentParserService).GetMethod(
            "SanitizeRetrievedContextForTemplateExtraction",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method, "SanitizeRetrievedContextForTemplateExtraction method not found");
        return (string)method.Invoke(null, new object?[] { context })!;
    }

    [TestMethod]
    public void SanitizeRetrievedContextForTemplateExtraction_RemovesProceduralPoison()
    {
        var context = string.Join("\n", new[]
        {
            "The system shall monitor mission power rails within specified limits.",
            "Recommended setup guidance should always sequence bench supplies before enabling any functional test rail.",
            "The interface shall verify startup timing within 5 seconds."
        });

        var sanitized = CallSanitizeRetrievedContext(context);

        Assert.IsFalse(
            sanitized.Contains("Recommended setup guidance should always sequence bench supplies", System.StringComparison.OrdinalIgnoreCase),
            "Procedural poison guidance should be removed from retrieval context.");
    }

    [TestMethod]
    public void SanitizeRetrievedContextForTemplateExtraction_KeepsExplicitVerificationClause()
    {
        var context = "Recommended procedure: The test system shall verify output regulation within tolerance under nominal load.";

        var sanitized = CallSanitizeRetrievedContext(context);

        Assert.IsTrue(
            sanitized.Contains("shall verify output regulation within tolerance", System.StringComparison.OrdinalIgnoreCase),
            "Explicit verification clauses should be preserved even when procedural terms appear.");
    }

    [TestMethod]
    public void SanitizeRetrievedContextForTemplateExtraction_PreservesNeutralTechnicalContent()
    {
        var context = "The system shall maintain +3.3VDC within the range [3.17, 3.43] VDC for at least 110 ms.";

        var sanitized = CallSanitizeRetrievedContext(context);

        Assert.AreEqual(context, sanitized, "Neutral technical content should remain unchanged.");
    }

    [TestMethod]
    public void SanitizeRetrievedContextForTemplateExtraction_PreservesTechnicalShouldAlwaysClause()
    {
        var context = "The system should always maintain coolant pressure between 40 and 55 psi during continuous operation.";

        var sanitized = CallSanitizeRetrievedContext(context);

        Assert.AreEqual(context, sanitized, "Technical 'should always' clauses without procedural setup cues should be preserved.");
    }

    [TestMethod]
    public void SanitizeRetrievedContextForTemplateExtraction_PreservesNormativeTechnicalClauseInsideRecommendedProcedureLine()
    {
        var context = "Recommended procedure: The system shall maintain coolant pressure between 40 and 55 psi during continuous operation.";

        var sanitized = CallSanitizeRetrievedContext(context);

        Assert.AreEqual(
            context,
            sanitized,
            "Normative technical clauses should be preserved even when recommendation/procedure wording appears in the same line.");
    }

    [TestMethod]
    public void SanitizeRetrievedContextForTemplateExtraction_PreservesMixedLineWithRecommendationAndNumericConstraint()
    {
        var context = "Recommended setup guidance: Interface module shall maintain +3.3VDC within the range [3.17, 3.43] VDC for at least 110 ms.";

        var sanitized = CallSanitizeRetrievedContext(context);

        Assert.AreEqual(
            context,
            sanitized,
            "Lines containing recommendation wording should still be retained when they carry strong normative numeric constraints.");
    }

    [TestMethod]
    public void SanitizeRetrievedContextForTemplateExtraction_RemovesPoisonLineButKeepsAdjacentValidRequirement()
    {
        var validRequirement = "The test system shall verify voltage regulation within tolerance limits.";
        var poison = "Recommended setup guidance should always sequence bench supplies before enabling any functional test rail.";
        var context = string.Join("\n", new[] { poison, validRequirement });

        var sanitized = CallSanitizeRetrievedContext(context);

        Assert.IsFalse(
            sanitized.Contains(poison, System.StringComparison.OrdinalIgnoreCase),
            "Procedural poison line should be removed.");
        Assert.IsTrue(
            sanitized.Contains(validRequirement, System.StringComparison.OrdinalIgnoreCase),
            "Adjacent valid normative requirement should be retained.");
    }

    [TestMethod]
    public void SanitizeRetrievedContextForTemplateExtraction_PreservesVerificationLineWithRecommendationWording()
    {
        var context = "Recommended procedure: The production test system shall verify startup status bits are reported within 2 seconds.";

        var sanitized = CallSanitizeRetrievedContext(context);

        Assert.AreEqual(
            context,
            sanitized,
            "Explicit verification clauses should remain even when recommendation wording is present.");
    }
}
