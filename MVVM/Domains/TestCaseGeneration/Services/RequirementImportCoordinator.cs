// TestCaseEditorApp/Import/RequirementImportCoordinator.cs
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services
{
    public sealed class RequirementImportCoordinator
    {
        private readonly RequirementService _wordService;
        private readonly IReadOnlyList<IRequirementEnricher> _enrichers;

        public RequirementImportCoordinator(RequirementService wordService,
                                            IEnumerable<IRequirementEnricher>? enrichers = null)
        {
            _wordService = wordService ?? throw new ArgumentNullException(nameof(wordService));
            _enrichers = (enrichers ?? Enumerable.Empty<IRequirementEnricher>()).ToList();
        }

        public List<Requirement> ImportFromWord(string wordPath)
        {
            var reqs = _wordService.ImportRequirementsFromWord(wordPath);
            foreach (var e in _enrichers)
                e.Enrich(reqs);
            return reqs;
        }
    }
}

