using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Tests.Requirements
{
    [TestClass]
    public class RequirementGeneratedTestCaseSemanticsTests
    {
        [TestMethod]
        public void HasGeneratedTestCase_IsFalse_WhenOnlyCurrentResponseExists()
        {
            var requirement = new Requirement
            {
                Item = "REQ-100",
                Name = "Test requirement"
            };

            requirement.SaveResponse("This is a draft LLM output only.");

            Assert.IsFalse(
                requirement.HasGeneratedTestCase,
                "HasGeneratedTestCase must not be true for CurrentResponse-only legacy draft data.");
        }

        [TestMethod]
        public void HasGeneratedTestCase_IsTrue_WhenGeneratedTestCasesContainsItems()
        {
            var requirement = new Requirement
            {
                Item = "REQ-200",
                Name = "Requirement with test case"
            };

            requirement.GeneratedTestCases.Add(new TestCase
            {
                Id = "TC-1",
                Name = "Generated test case"
            });

            Assert.IsTrue(requirement.HasGeneratedTestCase);
        }
    }
}
