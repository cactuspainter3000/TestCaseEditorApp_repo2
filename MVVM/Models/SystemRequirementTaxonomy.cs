using System;
using System.Collections.Generic;
using System.Linq;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Represents the hierarchical taxonomy for system requirements categorization.
    /// Based on the A-N category system for avionics test engineering solutions.
    /// </summary>
    public class SystemRequirementTaxonomy
    {
        public static readonly SystemRequirementTaxonomy Default = new SystemRequirementTaxonomy();

        public List<RequirementCategory> Categories { get; }

        public SystemRequirementTaxonomy()
        {
            Categories = new List<RequirementCategory>
            {
                new RequirementCategory("A", "Mission and Scope", "System purpose, boundary, and operating contexts", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("A1", "System Purpose", "Outcomes the test system must enable"),
                    new RequirementSubcategory("A2", "System Boundary", "What is inside vs outside: instruments, fixtures, operators, external networks, UUT"),
                    new RequirementSubcategory("A3", "Supported UUT Variants", "Configurations, options, part numbers"),
                    new RequirementSubcategory("A4", "Operating Contexts", "Production, depot, engineering lab, calibration mode")
                }),

                new RequirementCategory("B", "External Interfaces and Connectivity", "System interfaces to UUT, networks, and environment", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("B1", "Electrical Interfaces to UUT", "Power, discrete I/O, analog, digital comms"),
                    new RequirementSubcategory("B2", "Mechanical Interface", "Mating, alignment, strain relief, keying"),
                    new RequirementSubcategory("B3", "Data Interfaces", "Station network, databases, PLM/PDM, file exchange"),
                    new RequirementSubcategory("B4", "Human Interfaces", "Operator UI, indicators, labels, prompts"),
                    new RequirementSubcategory("B5", "Safety/Facility Interfaces", "E-stop, mains, grounding, ESD, compressed air")
                }),

                new RequirementCategory("C", "Stimulus Capabilities", "What the test system can do to the UUT", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("C1", "Power Provisioning", "Voltage rails, current limits, sequencing"),
                    new RequirementSubcategory("C2", "Discrete Control", "Assert/deassert, timing, drive strength"),
                    new RequirementSubcategory("C3", "Analog Stimulus", "Levels, bandwidth, accuracy"),
                    new RequirementSubcategory("C4", "Digital Bus/Protocol Stimulation", "Frames/messages, timing constraints"),
                    new RequirementSubcategory("C5", "Fault Injection", "Open/short simulation, bus error injection")
                }),

                new RequirementCategory("D", "Measurement and Observability", "What the test system can see", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("D1", "Voltage/Current Measurement", "DC/AC measurement capabilities"),
                    new RequirementSubcategory("D2", "Discrete State Sensing", "Digital input monitoring"),
                    new RequirementSubcategory("D3", "Analog Measurement", "Accuracy, noise floor, bandwidth"),
                    new RequirementSubcategory("D4", "Digital Data Capture/Decode", "Protocol decode and analysis"),
                    new RequirementSubcategory("D5", "Timing Measurement", "Latency, jitter, response times"),
                    new RequirementSubcategory("D6", "Built-in Self Test", "Sanity checks, instrument health")
                }),

                new RequirementCategory("E", "Evaluation and Decision Logic", "How measurements become pass/fail verdicts", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("E1", "Acceptance Criteria Handling", "Limits, tolerances, units, conversions"),
                    new RequirementSubcategory("E2", "Rule Execution", "Sequencing logic, branching, conditional tests"),
                    new RequirementSubcategory("E3", "Verdict Generation", "Test-level, requirement-level, unit-level"),
                    new RequirementSubcategory("E4", "Anomaly Handling", "Retries, confirmation steps, operator interventions"),
                    new RequirementSubcategory("E5", "Uncertainty Handling", "Margins, measurement uncertainty policy")
                }),

                new RequirementCategory("F", "Test Flow Control and Automation", "Test execution control and state management", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("F1", "Test Selection and Execution", "Individual test looping, subsets, rework"),
                    new RequirementSubcategory("F2", "State Management", "Startup, shutdown, safe states"),
                    new RequirementSubcategory("F3", "Timing Control", "Delays, settling times, timeouts"),
                    new RequirementSubcategory("F4", "Resource Scheduling", "Instrument access, exclusive resources"),
                    new RequirementSubcategory("F5", "Simulation/Emulation Modes", "Dry run, no-power mode, debug mode")
                }),

                new RequirementCategory("G", "Safety, Protection, and Containment", "Protection of UUT, station, and operators", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("G1", "UUT Protection", "Overvoltage/overcurrent, miswire protection, safe sequencing"),
                    new RequirementSubcategory("G2", "Test Station Protection", "Fault isolation from UUT"),
                    new RequirementSubcategory("G3", "Operator Safety", "Hazards, interlocks, E-stop behavior"),
                    new RequirementSubcategory("G4", "Fault Containment", "No cascading failures, safe shutdown"),
                    new RequirementSubcategory("G5", "Environmental/ESD Controls", "Grounding, ESD handling steps")
                }),

                new RequirementCategory("H", "Configuration, Control, and Compatibility", "System configuration and variant management", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("H1", "UUT Identification", "Serial/part/rev, scanning, manual entry constraints"),
                    new RequirementSubcategory("H2", "Test Station Configuration", "Station identity, calibration status, cable/fixture identity"),
                    new RequirementSubcategory("H3", "Procedure/Software Version Control", "Approved versions, change control hooks"),
                    new RequirementSubcategory("H4", "Variant Rules", "Which tests apply to which UUT configurations"),
                    new RequirementSubcategory("H5", "Parameter Management", "Limits tables, power tables, pin maps")
                }),

                new RequirementCategory("I", "Data, Evidence, and Traceability", "Trust requirements and data integrity", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("I1", "Results Recording", "What gets recorded, granularity"),
                    new RequirementSubcategory("I2", "Traceability Links", "Results ↔ test case ↔ requirement ↔ configuration"),
                    new RequirementSubcategory("I3", "Data Integrity", "Checksums, tamper evidence"),
                    new RequirementSubcategory("I4", "Data Retention and Retrieval", "Archive, search, export formats"),
                    new RequirementSubcategory("I5", "Reporting", "Datasheet generation, summaries, failure reports")
                }),

                new RequirementCategory("J", "Diagnostics and Maintainability", "Station maintenance and troubleshooting", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("J1", "Station Health Monitoring", "Instrument status, calibration validity, self-checks"),
                    new RequirementSubcategory("J2", "Troubleshooting Support", "Logs, guided diagnostics, fault codes"),
                    new RequirementSubcategory("J3", "Maintenance Workflow", "Replaceable items, calibration prompts"),
                    new RequirementSubcategory("J4", "Supportability", "Remote support, debug captures, reproducing runs")
                }),

                new RequirementCategory("K", "Performance and Throughput", "System performance characteristics", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("K1", "Cycle Time", "Max test duration, bottlenecks"),
                    new RequirementSubcategory("K2", "Availability/Reliability", "Uptime expectations, MTTR targets"),
                    new RequirementSubcategory("K3", "Scalability", "Multiple stations, parallelization"),
                    new RequirementSubcategory("K4", "Responsiveness", "UI latency, measurement turnaround")
                }),

                new RequirementCategory("L", "Usability and Human Factors", "Operator interface and user experience", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("L1", "Operator Guidance", "Clear prompts, sequencing, confirmations"),
                    new RequirementSubcategory("L2", "Error Prevention", "Poka-yoke: keying, checks, forced steps"),
                    new RequirementSubcategory("L3", "Training Burden", "Ease-of-use constraints"),
                    new RequirementSubcategory("L4", "Accessibility/Ergonomics", "Accessibility and ergonomic constraints")
                }),

                new RequirementCategory("M", "Security and Access Control", "Authentication, authorization, and data protection", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("M1", "Authentication/Authorization", "User access control"),
                    new RequirementSubcategory("M2", "Audit Logging", "Security event tracking"),
                    new RequirementSubcategory("M3", "Data Protection", "At rest/in transit protection"),
                    new RequirementSubcategory("M4", "Secure Update and Deployment", "Signed builds, controlled installs")
                }),

                new RequirementCategory("N", "Compliance and Standards Constraints", "Regulatory and process compliance", new List<RequirementSubcategory>
                {
                    new RequirementSubcategory("N1", "Internal Process Compliance", "Marking, release, documentation policies"),
                    new RequirementSubcategory("N2", "External/Customer Constraints", "Customer-specific requirements"),
                    new RequirementSubcategory("N3", "Evidence Requirements", "Auditability rules")
                })
            };
        }

        /// <summary>
        /// Find a category by its code (e.g., "C", "D1")
        /// </summary>
        public RequirementCategory? FindCategory(string code)
        {
            return Categories.FirstOrDefault(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Find a subcategory by its code (e.g., "C1", "D3")
        /// </summary>
        public RequirementSubcategory? FindSubcategory(string code)
        {
            foreach (var category in Categories)
            {
                var subcategory = category.Subcategories.FirstOrDefault(s => 
                    s.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                if (subcategory != null)
                    return subcategory;
            }
            return null;
        }

        /// <summary>
        /// Get all allocation targets (subsystems) that requirements can be assigned to
        /// </summary>
        public static List<string> GetAllocationTargets()
        {
            return new List<string>
            {
                "PowerSubsystem",
                "InstrumentationSubsystem", 
                "SoftwareSubsystem",
                "InterconnectSubsystem",
                "ProtectionSubsystem",
                "OperatorWorkflowSubsystem",
                "DataHandlingSubsystem",
                "SafetySubsystem"
            };
        }

        /// <summary>
        /// Validate if a requirement text belongs at system level vs lower levels
        /// </summary>
        public ValidationResult ValidateRequirementLevel(string requirementText)
        {
            var text = requirementText?.ToLower() ?? "";

            // Reject test procedure statements
            if (text.Contains("test passes") || text.Contains("verify test") || text.Contains("perform step"))
            {
                return new ValidationResult(false, "Test procedure statement, not system capability", "TestArtifact");
            }

            // Reject pure design constraints  
            if (text.Contains("use cable") || text.Contains(" ohm ") || text.Contains("connector part"))
            {
                return new ValidationResult(false, "Design constraint, not system requirement", "DesignConstraint");
            }

            // Reject procedural instructions
            if (text.Contains("operator shall") && (text.Contains("press") || text.Contains("connect")))
            {
                return new ValidationResult(false, "Operator procedure, not system capability", "OperatorProcedure");
            }

            return new ValidationResult(true, "Valid system-level requirement", "SystemRequirement");
        }

        /// <summary>
        /// Validates if the given category code exists in the taxonomy
        /// </summary>
        public bool IsValidCategory(string categoryCode)
        {
            return !string.IsNullOrWhiteSpace(categoryCode) && 
                   Categories.Any(c => c.Code.Equals(categoryCode, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Validates if the given subcategory code exists in the taxonomy
        /// </summary>
        public bool IsValidSubcategory(string subcategoryCode)
        {
            return !string.IsNullOrWhiteSpace(subcategoryCode) && 
                   Categories.SelectMany(c => c.Subcategories).Any(sc => sc.Code.Equals(subcategoryCode, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets a category by its code
        /// </summary>
        public RequirementCategory GetCategory(string categoryCode)
        {
            return Categories.FirstOrDefault(c => c.Code.Equals(categoryCode, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets a subcategory by its code
        /// </summary>
        public RequirementSubcategory GetSubcategory(string subcategoryCode)
        {
            return Categories.SelectMany(c => c.Subcategories)
                           .FirstOrDefault(sc => sc.Code.Equals(subcategoryCode, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Represents a top-level requirement category (A-N)
    /// </summary>
    public class RequirementCategory
    {
        public string Code { get; }
        public string Name { get; }
        public string Description { get; }
        public List<RequirementSubcategory> Subcategories { get; }

        public RequirementCategory(string code, string name, string description, List<RequirementSubcategory> subcategories)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Subcategories = subcategories ?? new List<RequirementSubcategory>();
        }

        public override string ToString() => $"{Code}. {Name}";
    }

    /// <summary>
    /// Represents a subcategory within a requirement category (e.g., C1, D3)
    /// </summary>
    public class RequirementSubcategory
    {
        public string Code { get; }
        public string Name { get; }
        public string Description { get; }

        public RequirementSubcategory(string code, string name, string description)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }

        public override string ToString() => $"{Code}. {Name}";
    }

    /// <summary>
    /// Result of validating if a requirement belongs at system level
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; }
        public string Reason { get; }
        public string SuggestedLevel { get; }

        public ValidationResult(bool isValid, string reason, string suggestedLevel)
        {
            IsValid = isValid;
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
            SuggestedLevel = suggestedLevel ?? throw new ArgumentNullException(nameof(suggestedLevel));
        }
    }
}