using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Tests;

[TestClass]
public class JamaRequirementMapperTests
{
    [TestMethod]
    public void MapFromKv_ComprehensiveFieldCoverage_PopulatesHandledJamaFields()
    {
        var requirement = new Requirement();
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Item ID"] = "DECAGON-REQ_RC-999",
            ["Name"] = "Field Coverage Requirement",
            ["Requirement Description"] = "The test system shall preserve all expected Jama fields.",
            ["Global ID"] = "COL02-REQ_RC-99999999",
            ["API ID"] = "123456",
            ["Item Type"] = "193",
            ["DOORS ID"] = "DOORS-42",
            ["Item Path"] = "/Project/Requirements/Field Coverage",
            ["Project"] = "636",
            ["Project Defined"] = "True",
            ["Release"] = "R1",
            ["Relationship Status"] = "Linked",
            ["DOORS Relationship"] = "Source",
            ["Version"] = "2.1",
            ["Current version"] = "2.1.0",
            ["Last Activity Date"] = "2026-01-22 13:30:49",
            ["Modified Date"] = "2026-01-20 15:15:15",
            ["Created Date"] = "2025-12-31 09:00:00",
            ["Locked"] = "false",
            ["Last Locked"] = "2026-01-21 08:00:00",
            ["Last Locked By"] = "field.owner",
            ["Created By"] = "creator.user",
            ["Modified By"] = "modifier.user",
            ["Derived Requirement"] = "No",
            ["Export Controlled"] = "No",
            ["Customer ID"] = "CUST-7",
            ["FDAL"] = "Class II",
            ["Key Characteristics"] = "Safety Critical",
            ["Heading"] = "Field Coverage Heading",
            ["Rationale"] = "Needed for Jama round-trip validation.",
            ["Compliance Rationale"] = "Traceability required.",
            ["Change Driver"] = "LLM integration",
            ["Allocation/s"] = "System; Hardware",
            ["Status"] = "Approved",
            ["Requirement Type"] = "Functional",
            ["Safety Requirement"] = "None",
            ["Safety Rationale"] = "N/A",
            ["Security Requirement"] = "None",
            ["Security Rationale"] = "N/A",
            ["Validation Method/s"] = "Traceability, Review",
            ["Validation Evidence"] = "Evidence text",
            ["Validation Conclusion"] = "Conforms",
            ["Verification Method/s"] = "Analysis, Test",
            ["Robust Requirement"] = "Yes",
            ["Robust Rationale"] = "Field coverage required",
            ["Tags"] = "alpha;beta/gamma and delta",
            ["Upstream Relationships"] = "UP-1",
            ["Relationships"] = "REL-1",
            ["Synchronized Items"] = "SYNC-1",
            ["Comments"] = "COMMENT-1",
            ["# of Downstream Relationships"] = "4",
            ["# of Upstream Relationships"] = "3",
            ["Connected Users"] = "2",
            ["# of Attachments"] = "1",
            ["# of Comments"] = "6",
            ["# of Links"] = "7"
        };

        JamaRequirementMapper.MapFromKv(requirement, kv);

        Assert.AreEqual("DECAGON-REQ_RC-999", requirement.Item);
        Assert.AreEqual("Field Coverage Requirement", requirement.Name);
        Assert.AreEqual("The test system shall preserve all expected Jama fields.", requirement.Description);
        Assert.AreEqual("COL02-REQ_RC-99999999", requirement.GlobalId);
        Assert.AreEqual("123456", requirement.ApiId);
        Assert.AreEqual("193", requirement.ItemType);
        Assert.AreEqual("DOORS-42", requirement.DoorsId);
        Assert.AreEqual("/Project/Requirements/Field Coverage", requirement.ItemPath);
        Assert.AreEqual("636", requirement.Project);
        Assert.AreEqual("True", requirement.ProjectDefined);
        Assert.AreEqual("R1", requirement.Release);
        Assert.AreEqual("Linked", requirement.RelationshipStatus);
        Assert.AreEqual("Source", requirement.DoorsRelationship);
        Assert.AreEqual("2.1", requirement.Version);
        Assert.AreEqual("2.1.0", requirement.CurrentVersion);
        Assert.AreEqual(DateTime.Parse("2026-01-22 13:30:49"), requirement.LastActivityDate);
        Assert.AreEqual(DateTime.Parse("2026-01-20 15:15:15"), requirement.ModifiedDate);
        Assert.AreEqual(DateTime.Parse("2025-12-31 09:00:00"), requirement.CreatedDate);
        Assert.AreEqual("false", requirement.Locked);
        Assert.AreEqual(DateTime.Parse("2026-01-21 08:00:00"), requirement.LastLocked);
        Assert.AreEqual("field.owner", requirement.LastLockedBy);
        Assert.AreEqual("creator.user", requirement.CreatedBy);
        Assert.AreEqual("modifier.user", requirement.ModifiedBy);
        Assert.AreEqual("No", requirement.DerivedRequirement);
        Assert.AreEqual("No", requirement.ExportControlled);
        Assert.AreEqual("CUST-7", requirement.CustomerId);
        Assert.AreEqual("Class II", requirement.Fdal);
        Assert.AreEqual("Safety Critical", requirement.KeyCharacteristics);
        Assert.AreEqual("Field Coverage Heading", requirement.Heading);
        Assert.AreEqual("Needed for Jama round-trip validation.", requirement.Rationale);
        Assert.AreEqual("Traceability required.", requirement.ComplianceRationale);
        Assert.AreEqual("LLM integration", requirement.ChangeDriver);
        Assert.AreEqual("System; Hardware", requirement.Allocations);
        Assert.AreEqual("Approved", requirement.Status);
        Assert.AreEqual("Functional", requirement.RequirementType);
        Assert.AreEqual("None", requirement.SafetyRequirement);
        Assert.AreEqual("N/A", requirement.SafetyRationale);
        Assert.AreEqual("None", requirement.SecurityRequirement);
        Assert.AreEqual("N/A", requirement.SecurityRationale);
        Assert.AreEqual("Traceability, Review", requirement.ValidationMethodRaw);
        CollectionAssert.AreEqual(new List<ValidationMethod> { ValidationMethod.Traceability, ValidationMethod.Review }, requirement.ValidationMethods);
        Assert.AreEqual("Evidence text", requirement.ValidationEvidence);
        Assert.AreEqual("Conforms", requirement.ValidationConclusion);
        Assert.AreEqual("Analysis, Test", requirement.VerificationMethodRaw);
        CollectionAssert.AreEqual(new List<VerificationMethod> { VerificationMethod.Analysis, VerificationMethod.Test }, requirement.VerificationMethods);
        Assert.AreEqual(VerificationMethod.Analysis, requirement.Method);
        Assert.AreEqual("Yes", requirement.RobustRequirement);
        Assert.AreEqual("Field coverage required", requirement.RobustRationale);
        Assert.AreEqual("alpha;beta;gamma;delta", requirement.Tags);
        CollectionAssert.AreEqual(new List<string> { "alpha", "beta", "gamma", "delta" }, requirement.TagList);
        Assert.AreEqual("UP-1", requirement.UpstreamRelationshipsText);
        Assert.AreEqual("REL-1", requirement.RelationshipsText);
        Assert.AreEqual("SYNC-1", requirement.SynchronizedItemsText);
        Assert.AreEqual("COMMENT-1", requirement.CommentsText);
        Assert.AreEqual(4, requirement.NumberOfDownstreamRelationships);
        Assert.AreEqual(3, requirement.NumberOfUpstreamRelationships);
        Assert.AreEqual(2, requirement.ConnectedUsers);
        Assert.AreEqual(1, requirement.NumberOfAttachments);
        Assert.AreEqual(6, requirement.NumberOfComments);
        Assert.AreEqual(7, requirement.NumberOfLinks);
    }
}