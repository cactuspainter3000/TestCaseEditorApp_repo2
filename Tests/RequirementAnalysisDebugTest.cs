using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services.Parsing;

namespace TestCaseEditorApp.Tests
{
    /// <summary>
    /// Debug test to diagnose incomplete analysis payloads
    /// </summary>
    [TestClass]
    public class RequirementAnalysisDebugTest
    {
        private JsonResponseParser _parser;

        [TestInitialize]
        public void Setup()
        {
            _parser = new JsonResponseParser();
        }

        /// <summary>
        /// Test Case 1: Complete payload - what the prompt expects
        /// </summary>
        [TestMethod]
        public void TestCompletePayload_ShouldParseAllFields()
        {
            var completeJson = @"{
  ""OriginalQualityScore"": 7,
  ""HallucinationCheck"": ""NO_FABRICATION"",
  ""Issues"": [
    {
      ""Category"": ""Testability"",
      ""Severity"": ""High"",
      ""Description"": ""Missing acceptance criteria"",
      ""Fix"": ""Added measurable thresholds""
    }
  ],
  ""Recommendations"": [
    {
      ""Category"": ""Testability"",
      ""Description"": ""Specify pass/fail criteria"",
      ""SuggestedEdit"": ""The system shall respond within 500ms""
    }
  ],
  ""ImprovedRequirement"": ""The system shall respond within 500ms with 99.9% reliability"",
  ""FreeformFeedback"": ""Well-structured requirement""
}";

            var result = _parser.ParseResponse(completeJson, "REQ-001");

            Assert.IsNotNull(result);
            Assert.AreEqual(7, result.OriginalQualityScore);
            Assert.AreEqual(1, result.Issues.Count);
            Assert.AreEqual(1, result.Recommendations.Count);
            Assert.IsNotNull(result.ImprovedRequirement);
            Console.WriteLine($"✅ Complete: Score={result.OriginalQualityScore}, Issues={result.Issues.Count}, Recs={result.Recommendations.Count}");
        }

        /// <summary>
        /// Test Case 2: Score-only payload - the incomplete issue
        /// </summary>
        [TestMethod]
        public void TestScoreOnlyPayload_ShouldStillParse()
        {
            var scoreOnlyJson = @"{
  ""OriginalQualityScore"": 5,
  ""HallucinationCheck"": ""NO_FABRICATION""
}";

            var result = _parser.ParseResponse(scoreOnlyJson, "REQ-002");

            Assert.IsNotNull(result);
            Assert.AreEqual(5, result.OriginalQualityScore);
            Assert.AreEqual(0, result.Issues.Count);
            Assert.AreEqual(0, result.Recommendations.Count);
            Console.WriteLine($"⚠️  Score-only: Score={result.OriginalQualityScore}, Issues={result.Issues.Count}, Recs={result.Recommendations.Count}");
        }

        /// <summary>
        /// Test Case 3: Legacy schema with top-level issue buckets
        /// </summary>
        [TestMethod]
        public void TestLegacyIssueSchema_ShouldParseFromTopLevel()
        {
            var legacyJson = @"{
  ""OriginalQualityScore"": 6,
  ""ClarityIssues"": [""Terms undefined""],
  ""TestabilityIssues"": [""No pass/fail criteria""]
}";

            var result = _parser.ParseResponse(legacyJson, "REQ-003");

            Assert.IsNotNull(result);
            Assert.AreEqual(6, result.OriginalQualityScore);
            Assert.IsTrue(result.Issues.Count > 0, "Should parse legacy issue buckets");
            Console.WriteLine($"📋 Legacy Issues: Score={result.OriginalQualityScore}, Issues={result.Issues.Count}");
        }

        /// <summary>
        /// Test Case 4: Missing Issues field (common LLM error)
        /// </summary>
        [TestMethod]
        public void TestMissingIssuesField_ShouldNotFail()
        {
            var missingIssuesJson = @"{
  ""OriginalQualityScore"": 7,
  ""Recommendations"": [
    {
      ""Category"": ""Clarity"",
      ""Description"": ""Clarify scope"",
      ""SuggestedEdit"": ""The system shall...""
    }
  ]
}";

            var result = _parser.ParseResponse(missingIssuesJson, "REQ-004");

            Assert.IsNotNull(result);
            Assert.AreEqual(7, result.OriginalQualityScore);
            Assert.AreEqual(0, result.Issues.Count);
            Assert.AreEqual(1, result.Recommendations.Count);
            Console.WriteLine($"🔄 Missing Issues: Score={result.OriginalQualityScore}, Issues={result.Issues.Count}, Recs={result.Recommendations.Count}");
        }

        /// <summary>
        /// Test Case 5: Nested schema variation (results in AdditionalFeedback)
        /// </summary>
        [TestMethod]
        public void TestNestedFeedbackSchema_ShouldExtractFromAdditionalFeedback()
        {
            var nestedJson = @"{
  ""OriginalQualityScore"": 8,
  ""AdditionalFeedback"": {
    ""Issues"": [
      {
        ""Category"": ""Completeness"",
        ""Severity"": ""Medium"",
        ""Description"": ""Missing context"",
        ""Fix"": ""Added context""
      }
    ],
    ""ActionableImprovements"": [
      {
        ""Category"": ""Actionability"",
        ""Description"": ""Make it testable"",
        ""SuggestedEdit"": ""Specify acceptance criteria""
      }
    ]
  }
}";

            var result = _parser.ParseResponse(nestedJson, "REQ-005");

            Assert.IsNotNull(result);
            Assert.AreEqual(8, result.OriginalQualityScore);
            Assert.IsTrue(result.Issues.Count > 0, "Should extract from AdditionalFeedback.Issues");
            Assert.IsTrue(result.Recommendations.Count > 0, "Should extract from AdditionalFeedback.ActionableImprovements");
            Console.WriteLine($"🔀 Nested Schema: Score={result.OriginalQualityScore}, Issues={result.Issues.Count}, Recs={result.Recommendations.Count}");
        }

        /// <summary>
        /// Test Case 6: Case sensitivity issue - lowercase "issues" instead of "Issues"
        /// </summary>
        [TestMethod]
        public void TestCaseSensitivityIssue_ShouldHandleBothCases()
        {
            var lowercaseJson = @"{
  ""originalqualityscore"": 5,
  ""issues"": [
    {
      ""category"": ""Testability"",
      ""description"": ""Missing criteria""
    }
  ]
}";

            var result = _parser.ParseResponse(lowercaseJson, "REQ-006");

            // If parser doesn't handle case-insensitivity, this will show us
            if (result != null)
            {
                Console.WriteLine($"✅ Case handling: Score={result.OriginalQualityScore}, Issues={result.Issues.Count}");
            }
            else
            {
                Console.WriteLine("❌ CASE SENSITIVITY ISSUE: Parser failed on lowercase JSON");
            }
        }
    }
}
