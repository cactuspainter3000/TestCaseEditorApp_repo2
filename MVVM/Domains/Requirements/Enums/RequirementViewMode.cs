namespace TestCaseEditorApp.MVVM.Domains.Requirements.Enums
{
    /// <summary>
    /// Unified view modes for requirements display (source-agnostic).
    /// Replaces the fragmented SupportView enum with clean 4-tab architecture.
    /// </summary>
    public enum RequirementViewMode
    {
        /// <summary>
        /// Details view: Metadata chips + requirement text + supplemental content.
        /// Combines Meta/Metadata/RichContent functionality adaptively based on data source.
        /// Always available.
        /// </summary>
        Details,

        /// <summary>
        /// Tables view: Table analysis and manipulation.
        /// Available when requirement.Tables.Any() is true.
        /// Source-agnostic - works for both Jama structured data and document parsed tables.
        /// </summary>
        Tables,

        /// <summary>
        /// Analysis view: LLM analysis results and quality scoring.
        /// Always available for any requirement.
        /// Source-agnostic analysis functionality.
        /// </summary>
        Analysis,

        /// <summary>
        /// Requirements Scraper: User-initiated requirement discovery from project attachments.
        /// Always available - provides workflow for finding additional requirements.
        /// User controls when attachment scanning occurs (no automatic scanning).
        /// </summary>
        RequirementsScraper
    }
}