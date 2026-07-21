using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests
{
    [TestClass]
    public class RequirementExtractionProgressTrackerTests
    {
        [TestMethod]
        public void AdvanceFromMessage_UsesMonotonicStageProgress()
        {
            var tracker = new RequirementExtractionProgressTracker();

            var preparing = tracker.AdvanceFromMessage("Preparing to extract requirements from test.docx...");
            var downloading = tracker.AdvanceFromMessage("Downloading attachment (120KB)...");
            var indexing = tracker.AdvanceFromMessage("Preparing extraction-aware document index for analysis...");
            var structured = tracker.AdvanceFromMessage("Using structured extraction with quality validation...");
            var analyzing = tracker.AdvanceFromMessage("AI analyzing document with structured output...");
            var found = tracker.AdvanceFromMessage("Found 8 requirements: 8 extracted + 0 derived");
            var stale = tracker.AdvanceFromMessage("Preparing to extract requirements from test.docx...");

            Assert.AreEqual(3, preparing);
            Assert.AreEqual(14, downloading);
            Assert.AreEqual(28, indexing);
            Assert.AreEqual(46, structured);
            Assert.AreEqual(60, analyzing);
            Assert.AreEqual(84, found);
            Assert.AreEqual(found, stale);
        }

        [TestMethod]
        public void AdvanceFromMessage_MapsSaveLoopProgressIntoFinalBand()
        {
            var tracker = new RequirementExtractionProgressTracker();

            var saveStart = tracker.AdvanceFromMessage("Saving 12 extracted requirements to Jama...");
            var saveHalf = tracker.AdvanceFromMessage("Jama save progress: 6/12 processed, 0 failed");
            var saveDone = tracker.AdvanceFromMessage("Jama save progress: 12/12 processed, 0 failed");
            var retry = tracker.AdvanceFromMessage("Retry save progress: 1/2 processed, 0 failed");
            var saved = tracker.AdvanceFromMessage("Saved 12 extracted requirements to Jama");

            Assert.AreEqual(88, saveStart);
            Assert.AreEqual(93, saveHalf);
            Assert.AreEqual(98, saveDone);
            Assert.AreEqual(98, retry);
            Assert.AreEqual(98, saved);
        }

        [TestMethod]
        public void DiscoveryAndComplete_AdvanceWithoutFakeInterpolation()
        {
            var tracker = new RequirementExtractionProgressTracker();

            tracker.AdvanceFromMessage("AI analyzing document with structured output...");
            var firstDiscovery = tracker.AdvanceFromDiscoveryCount(1);
            var fifthDiscovery = tracker.AdvanceFromDiscoveryCount(5);
            var complete = tracker.Complete();

            Assert.AreEqual(60, firstDiscovery);
            Assert.AreEqual(63, fifthDiscovery);
            Assert.AreEqual(100, complete);
        }
    }
}