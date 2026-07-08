using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;

namespace TestCaseEditorApp.Tests
{
    [TestClass]
    public class IncoseConsistentContentCheckerTests
    {
        private IncoseConsistentContentChecker _checker;

        [TestInitialize]
        public void Setup()
        {
            _checker = new IncoseConsistentContentChecker();
        }

        // -----------------------------------------------------------------------
        // PASSING CASES — well-formed requirements
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Check_GeneralShallPattern_Passes()
        {
            var result = _checker.Check("The Test System shall generate a diagnostic report.");

            Assert.IsTrue(result.Passed);
            Assert.AreEqual(0, result.Issues.Count);
            Assert.AreEqual("General", result.RequirementType);
            Assert.AreEqual("Test System", result.DetectedActor);
            Assert.AreEqual("generate", result.DetectedAction);
        }

        [TestMethod]
        public void Check_EventDrivenPattern_Passes()
        {
            var result = _checker.Check("The operator interface shall display an alert when a fault condition is detected.");

            Assert.IsTrue(result.Passed);
            Assert.AreEqual(0, result.Issues.Count);
            Assert.AreEqual("Event-Driven", result.RequirementType);
        }

        [TestMethod]
        public void Check_StateDrivenPattern_Passes()
        {
            var result = _checker.Check("The system shall log all transactions while the audit mode is active.");

            Assert.IsTrue(result.Passed);
            Assert.AreEqual(0, result.Issues.Count);
            Assert.AreEqual("State-Driven", result.RequirementType);
        }

        [TestMethod]
        public void Check_OptionalFeaturePattern_Passes()
        {
            var result = _checker.Check("The system shall provide enhanced diagnostics where the extended diagnostic option is enabled.");

            Assert.IsTrue(result.Passed);
            Assert.AreEqual(0, result.Issues.Count);
            Assert.AreEqual("Optional-Feature", result.RequirementType);
        }

        [TestMethod]
        public void Check_GeneralWithTimingPattern_Passes()
        {
            var result = _checker.Check("The test system shall complete the self-test within 30 seconds.");

            Assert.IsTrue(result.Passed);
            Assert.AreEqual(0, result.Issues.Count);
            Assert.AreEqual("General + Timing", result.RequirementType);
        }

        [TestMethod]
        public void Check_EventDrivenWithTimingPattern_Passes()
        {
            var result = _checker.Check("The controller shall respond when a stop command is received within 500 ms.");

            Assert.IsTrue(result.Passed);
            Assert.AreEqual(0, result.Issues.Count);
            Assert.AreEqual("Event-Driven + Timing", result.RequirementType);
        }

        // -----------------------------------------------------------------------
        // ICC-002 — Missing obligation keyword
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Check_NoObligationKeyword_ReturnsHighSeverityIssue()
        {
            var result = _checker.Check("The system generates a report.");

            Assert.IsFalse(result.Passed);
            Assert.IsTrue(result.Issues.Count >= 1);
            Assert.IsTrue(result.Issues.Exists(i => i.Code == "ICC-002"));
            Assert.AreEqual("High", result.Issues.Find(i => i.Code == "ICC-002")!.Severity);
        }

        [TestMethod]
        public void Check_EmptyText_ReturnsICC001()
        {
            var result = _checker.Check("");

            Assert.IsFalse(result.Passed);
            Assert.IsTrue(result.Issues.Exists(i => i.Code == "ICC-001"));
        }

        [TestMethod]
        public void Check_WhitespaceOnly_ReturnsICC001()
        {
            var result = _checker.Check("   ");

            Assert.IsFalse(result.Passed);
            Assert.IsTrue(result.Issues.Exists(i => i.Code == "ICC-001"));
        }

        // -----------------------------------------------------------------------
        // ICC-003 — Uses must/will/should instead of shall
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Check_UsesMustInsteadOfShall_ReturnsMediumWarning()
        {
            var result = _checker.Check("The system must generate a report.");

            // "must" without "shall" → ICC-003 but still has actor/action so only medium issue
            Assert.IsTrue(result.Issues.Exists(i => i.Code == "ICC-003"));
            Assert.AreEqual("Medium", result.Issues.Find(i => i.Code == "ICC-003")!.Severity);
        }

        [TestMethod]
        public void Check_UsesWillInsteadOfShall_ReturnsMediumWarning()
        {
            var result = _checker.Check("The test system will perform boundary scan.");

            Assert.IsTrue(result.Issues.Exists(i => i.Code == "ICC-003"));
        }

        [TestMethod]
        public void Check_UsesShouldInsteadOfShall_ReturnsMediumWarning()
        {
            var result = _checker.Check("The interface should display status.");

            Assert.IsTrue(result.Issues.Exists(i => i.Code == "ICC-003"));
        }

        // -----------------------------------------------------------------------
        // ICC-004 — Missing actor
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Check_ShallWithoutActor_ReturnsHighSeverityIssue()
        {
            var result = _checker.Check("Shall generate a report.");

            Assert.IsFalse(result.Passed);
            Assert.IsTrue(result.Issues.Exists(i => i.Code == "ICC-004"));
            Assert.AreEqual("High", result.Issues.Find(i => i.Code == "ICC-004")!.Severity);
        }

        [TestMethod]
        public void Check_ShallWithActorDetected_NoICC004()
        {
            var result = _checker.Check("The DHM system shall perform a self-test.");

            Assert.IsFalse(result.Issues.Exists(i => i.Code == "ICC-004"));
            Assert.AreEqual("DHM system", result.DetectedActor);
        }

        // -----------------------------------------------------------------------
        // ICC-005 — No action verb after "shall"
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Check_ShallFollowedByArticle_ReturnsHighSeverityIssue()
        {
            var result = _checker.Check("The system shall a report.");

            Assert.IsFalse(result.Passed);
            Assert.IsTrue(result.Issues.Exists(i => i.Code == "ICC-005"));
        }

        [TestMethod]
        public void Check_ShallFollowedByThe_ReturnsHighSeverityIssue()
        {
            var result = _checker.Check("The system shall the test.");

            Assert.IsFalse(result.Passed);
            Assert.IsTrue(result.Issues.Exists(i => i.Code == "ICC-005"));
        }

        [TestMethod]
        public void Check_ShallFollowedByVerb_NoICC005()
        {
            var result = _checker.Check("The system shall execute the test sequence.");

            Assert.IsFalse(result.Issues.Exists(i => i.Code == "ICC-005"));
            Assert.AreEqual("execute", result.DetectedAction);
        }

        // -----------------------------------------------------------------------
        // ICC-006 — Mixed when + while conditionals
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Check_MixedWhenAndWhile_ReturnsMediumIssue()
        {
            var result = _checker.Check("The system shall log an event when the trigger fires while the mode is active.");

            Assert.IsTrue(result.Issues.Exists(i => i.Code == "ICC-006"));
            Assert.AreEqual("Medium", result.Issues.Find(i => i.Code == "ICC-006")!.Severity);
        }

        [TestMethod]
        public void Check_OnlyWhen_NoMixedConditionalIssue()
        {
            var result = _checker.Check("The system shall raise an alarm when a threshold is exceeded.");

            Assert.IsFalse(result.Issues.Exists(i => i.Code == "ICC-006"));
        }

        [TestMethod]
        public void Check_OnlyWhile_NoMixedConditionalIssue()
        {
            var result = _checker.Check("The system shall monitor temperature while the reactor is operating.");

            Assert.IsFalse(result.Issues.Exists(i => i.Code == "ICC-006"));
        }

        // -----------------------------------------------------------------------
        // ICC-007 — Condition before "shall"
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Check_WhenBeforeShall_ReturnsLowIssue()
        {
            var result = _checker.Check("When a fault occurs, the system shall stop.");

            Assert.IsTrue(result.Issues.Exists(i => i.Code == "ICC-007"));
            Assert.AreEqual("Low", result.Issues.Find(i => i.Code == "ICC-007")!.Severity);
        }

        // -----------------------------------------------------------------------
        // RequirementType classification
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Check_ClassifiesStateDrivenWithTiming()
        {
            var result = _checker.Check("The system shall flush the buffer while processing is active within 100 ms.");

            Assert.AreEqual("State-Driven + Timing", result.RequirementType);
        }

        [TestMethod]
        public void Check_ClassifiesOptionalFeatureWithTiming()
        {
            var result = _checker.Check("The system shall enable verbose output where diagnostic mode is configured within 5 seconds.");

            Assert.AreEqual("Optional-Feature + Timing", result.RequirementType);
        }

        // -----------------------------------------------------------------------
        // CanonicalFormSuggestion
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Check_GeneralForm_CanonicalSuggestionContainsShall()
        {
            var result = _checker.Check("The system shall record the result.");

            StringAssert.Contains(result.CanonicalFormSuggestion, "shall");
        }

        [TestMethod]
        public void Check_EventDrivenForm_CanonicalSuggestionContainsWhen()
        {
            var result = _checker.Check("The system shall alert the operator when the threshold is exceeded.");

            StringAssert.Contains(result.CanonicalFormSuggestion, "when");
        }

        [TestMethod]
        public void Check_StateDrivenForm_CanonicalSuggestionContainsWhile()
        {
            var result = _checker.Check("The system shall poll the sensor while the device is online.");

            StringAssert.Contains(result.CanonicalFormSuggestion, "while");
        }

        // -----------------------------------------------------------------------
        // ToAnalysisIssues mapping
        // -----------------------------------------------------------------------

        [TestMethod]
        public void ToAnalysisIssues_MapsToConsistencyCategory()
        {
            var checkResult = _checker.Check("Shall generate a report.");
            var issues = _checker.ToAnalysisIssues(checkResult);

            Assert.IsTrue(issues.Count > 0);
            Assert.IsTrue(issues.TrueForAll(i => i.Category == "Consistency"));
        }

        [TestMethod]
        public void ToAnalysisIssues_IncludesCodeInDescription()
        {
            var checkResult = _checker.Check("Shall generate a report.");
            var issues = _checker.ToAnalysisIssues(checkResult);

            Assert.IsTrue(issues.TrueForAll(i => i.Description.Contains("ICC-")));
        }

        [TestMethod]
        public void ToAnalysisIssues_FixContainsSuggestion()
        {
            var checkResult = _checker.Check("Shall generate a report.");
            var issues = _checker.ToAnalysisIssues(checkResult);

            Assert.IsTrue(issues.TrueForAll(i => !string.IsNullOrWhiteSpace(i.Fix)));
        }

        [TestMethod]
        public void ToAnalysisIssues_WellFormedRequirement_ReturnsEmptyList()
        {
            var checkResult = _checker.Check("The test system shall execute the self-test sequence.");
            var issues = _checker.ToAnalysisIssues(checkResult);

            Assert.AreEqual(0, issues.Count);
        }

        // -----------------------------------------------------------------------
        // Real-world requirement examples
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Check_RealWorldATPRequirement_Passes()
        {
            var result = _checker.Check(
                "The DHM test system shall perform boundary scan coverage of all JTAG-accessible nodes when the test sequence is initiated.");

            Assert.IsTrue(result.Passed);
            Assert.AreEqual("Event-Driven", result.RequirementType);
        }

        [TestMethod]
        public void Check_RealWorldPerformanceRequirement_Passes()
        {
            var result = _checker.Check(
                "The test controller shall complete the full diagnostic cycle within 120 seconds.");

            Assert.IsTrue(result.Passed);
            Assert.AreEqual("General + Timing", result.RequirementType);
        }

        [TestMethod]
        public void Check_RealWorldVagueRequirement_DetectsMultipleIssues()
        {
            // Missing actor, using "must", no explicit action verb after must
            var result = _checker.Check("Reports must be generated.");

            Assert.IsFalse(result.Passed);
            Assert.IsTrue(result.Issues.Count >= 1);
        }
    }
}
