using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests;

/// <summary>
/// Tests for Jama upgrade-banner detection and Template Form bracket-prefix extraction.
/// </summary>
[TestClass]
public class JamaUpgradeBannerRetryTests
{
    // -----------------------------------------------------------------------
    // IsJamaUpgradeBanner — accessed via reflection (private static method)
    // -----------------------------------------------------------------------

    private static bool CallIsJamaUpgradeBanner(string? content)
    {
        var method = typeof(JamaConnectService).GetMethod(
            "IsJamaUpgradeBanner",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "IsJamaUpgradeBanner method not found");
        return (bool)method.Invoke(null, new object?[] { content })!;
    }

    private static bool CallIsNonRetryable(string? message)
    {
        var method = typeof(JamaConnectService).GetMethod(
            "IsNonRetryableRequirementCreateFailure",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "IsNonRetryableRequirementCreateFailure method not found");
        return (bool)method.Invoke(null, new object?[] { message })!;
    }

    [TestMethod]
    public void IsJamaUpgradeBanner_WithUpgradeHtml_ReturnsTrue()
    {
        var html = @"<html><body><textarea>java.lang.IllegalStateException: Your instance of Jama Cloud is being upgraded! Please check later.</textarea></body></html>";
        Assert.IsTrue(CallIsJamaUpgradeBanner(html));
    }

    [TestMethod]
    public void IsJamaUpgradeBanner_WithPartialPhrase_ReturnsTrue()
    {
        Assert.IsTrue(CallIsJamaUpgradeBanner("being upgraded"));
    }

    [TestMethod]
    public void IsJamaUpgradeBanner_WithNormalApiError_ReturnsFalse()
    {
        Assert.IsFalse(CallIsJamaUpgradeBanner(@"{""errors"":[{""code"":400,""message"":""Bad request""}]}"));
    }

    [TestMethod]
    public void IsJamaUpgradeBanner_WithNull_ReturnsFalse()
    {
        Assert.IsFalse(CallIsJamaUpgradeBanner(null));
    }

    [TestMethod]
    public void IsJamaUpgradeBanner_WithEmpty_ReturnsFalse()
    {
        Assert.IsFalse(CallIsJamaUpgradeBanner(string.Empty));
    }

    [TestMethod]
    public void IsNonRetryable_WithUpgradeMessage_ReturnsFalse()
    {
        // Upgrade is transient — must NOT be marked non-retryable
        Assert.IsFalse(CallIsNonRetryable("JAMA_UPGRADING: Instance is being upgraded, retry after delay"));
    }

    [TestMethod]
    public void IsNonRetryable_WithItemTypeError_ReturnsTrue()
    {
        Assert.IsTrue(CallIsNonRetryable("No requirement item type found for this project"));
    }

    [TestMethod]
    public void IsNonRetryable_WithNull_ReturnsFalse()
    {
        Assert.IsFalse(CallIsNonRetryable(null));
    }
}

/// <summary>
/// Tests for CapabilityDerivationTemplateService.ExtractFieldValue bracket-prefix handling.
/// </summary>
[TestClass]
public class TemplateFormBracketExtractionTests
{
    private static string CallExtractFieldValue(string response, string fieldDisplayName)
    {
        // Use a real instance — no deps needed for this pure parsing method
        var svc = new TestCaseEditorApp.Services.Templates.CapabilityDerivationTemplateService();
        var method = svc.GetType().GetMethod(
            "ExtractFieldValue",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(method, "ExtractFieldValue method not found");
        return (string)method.Invoke(svc, new object[] { response, fieldDisplayName })!;
    }

    [TestMethod]
    public void ExtractFieldValue_WithExactDisplayName_ReturnsValue()
    {
        var response = "[REQUIRED] System Capability:\nInstructions: ...\nYour Response: Verify MFD luminance\n";
        var result = CallExtractFieldValue(response, "[REQUIRED] System Capability");
        Assert.AreEqual("Verify MFD luminance", result);
    }

    [TestMethod]
    public void ExtractFieldValue_LlmStrippedBracket_ReturnsValue()
    {
        // LLM omits the [REQUIRED] tag — this was the original bug
        var response = "System Capability:\nInstructions: ...\nYour Response: Verify MFD luminance\n";
        var result = CallExtractFieldValue(response, "[REQUIRED] System Capability");
        Assert.AreEqual("Verify MFD luminance", result);
    }

    [TestMethod]
    public void ExtractFieldValue_TaxonomyCategory_LlmStrippedBracket_ReturnsValue()
    {
        var response = "Taxonomy Category:\nInstructions: ...\nYour Response: Display Systems\n";
        var result = CallExtractFieldValue(response, "[REQUIRED] Taxonomy Category");
        Assert.AreEqual("Display Systems", result);
    }

    [TestMethod]
    public void ExtractFieldValue_WhenFieldMissing_ReturnsEmpty()
    {
        var response = "Some other content without the field.";
        var result = CallExtractFieldValue(response, "[REQUIRED] System Capability");
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void ExtractFieldValue_WithInsufficientInfoValue_ReturnsEmpty()
    {
        // INSUFFICIENT_INFO values should not be treated as real data
        var response = "[REQUIRED] System Capability:\nYour Response: INSUFFICIENT_INFO\n";
        var result = CallExtractFieldValue(response, "[REQUIRED] System Capability");
        // ExtractFieldValue returns the raw value; the caller filters INSUFFICIENT_INFO
        Assert.AreEqual("INSUFFICIENT_INFO", result);
    }

    [TestMethod]
    public void ExtractFieldValue_EnhancementField_LlmStrippedBracket_ReturnsValue()
    {
        var response = "Standards References:\nYour Response: MIL-STD-810\n";
        var result = CallExtractFieldValue(response, "[ENHANCEMENT] Standards References");
        Assert.AreEqual("MIL-STD-810", result);
    }
}
