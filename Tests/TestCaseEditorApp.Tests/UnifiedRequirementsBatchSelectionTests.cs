using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests.Requirements
{
    [TestClass]
    public class UnifiedRequirementsBatchSelectionTests
    {
        [TestMethod]
        public void BatchSelection_DefaultsToFirstFiveRequirements()
        {
            var requirements = CreateRequirements(7);
            var persistence = new InMemoryPersistenceService();
            var viewModel = CreateViewModel(requirements, persistence, projectId: 101);

            var selectedIds = viewModel.BatchRequirementSelections
                .Where(x => x.IsSelected)
                .Select(x => x.Requirement.GlobalId)
                .ToList();

            CollectionAssert.AreEqual(
                new[] { "REQ-1", "REQ-2", "REQ-3", "REQ-4", "REQ-5" },
                selectedIds,
                "Expected default selection to include first 5 requirements.");
            Assert.AreEqual(5, viewModel.SelectedBatchCount);
            Assert.IsTrue(viewModel.AnalyzeSelectedBatchCommand.CanExecute(null));
        }

        [TestMethod]
        public void BatchSelection_PersistsAcrossViewModelInstances()
        {
            var persistence = new InMemoryPersistenceService();

            var requirementsFirst = CreateRequirements(7);
            var firstVm = CreateViewModel(requirementsFirst, persistence, projectId: 777);

            foreach (var item in firstVm.BatchRequirementSelections)
            {
                item.IsSelected = false;
            }

            firstVm.BatchRequirementSelections.Single(x => x.Requirement.GlobalId == "REQ-2").IsSelected = true;
            firstVm.BatchRequirementSelections.Single(x => x.Requirement.GlobalId == "REQ-6").IsSelected = true;

            firstVm.Dispose();

            var requirementsSecond = CreateRequirements(7);
            var secondVm = CreateViewModel(requirementsSecond, persistence, projectId: 777);

            var selectedIds = secondVm.BatchRequirementSelections
                .Where(x => x.IsSelected)
                .Select(x => x.Requirement.GlobalId)
                .OrderBy(x => x)
                .ToList();

            CollectionAssert.AreEqual(new[] { "REQ-2", "REQ-6" }, selectedIds);
        }

        [TestMethod]
        public void AnalyzeSelectedBatchCommand_DisablesWhenNothingSelected()
        {
            var requirements = CreateRequirements(3);
            var persistence = new InMemoryPersistenceService();
            var viewModel = CreateViewModel(requirements, persistence, projectId: 202);

            Assert.IsTrue(viewModel.AnalyzeSelectedBatchCommand.CanExecute(null));

            foreach (var item in viewModel.BatchRequirementSelections)
            {
                item.IsSelected = false;
            }

            Assert.AreEqual(0, viewModel.SelectedBatchCount);
            Assert.IsFalse(viewModel.AnalyzeSelectedBatchCommand.CanExecute(null));
        }

        private static UnifiedRequirementsMainViewModel CreateViewModel(
            ObservableCollection<Requirement> requirements,
            IPersistenceService persistence,
            int projectId)
        {
            var mediatorMock = new Mock<IRequirementsMediator>(MockBehavior.Loose);
            mediatorMock.SetupGet(m => m.Requirements).Returns(requirements);
            mediatorMock.SetupGet(m => m.CurrentRequirement).Returns((Requirement?)null);
            mediatorMock.SetupGet(m => m.CurrentProjectId).Returns(projectId);
            mediatorMock.SetupGet(m => m.CurrentProjectName).Returns("BatchSelectionProject");
            mediatorMock.Setup(m => m.GetCurrentRequirementIndex()).Returns(0);

            var workspaceContextMock = new Mock<IWorkspaceContext>(MockBehavior.Loose);

            var reqSearchLogger = new Mock<ILogger<RequirementsSearchAttachmentsViewModel>>();
            var reqSearchVm = new RequirementsSearchAttachmentsViewModel(
                mediatorMock.Object,
                workspaceContextMock.Object,
                reqSearchLogger.Object);

            var vmLogger = new Mock<ILogger<UnifiedRequirementsMainViewModel>>();
            var textEditingService = new Mock<ITextEditingDialogService>(MockBehavior.Loose);
            var navigationMediatorMock = new Mock<INavigationMediator>();

            return new UnifiedRequirementsMainViewModel(
                mediatorMock.Object,
                vmLogger.Object,
                persistence,
                textEditingService.Object,
                reqSearchVm,
                navigationMediatorMock.Object,
                null);
        }

        private static ObservableCollection<Requirement> CreateRequirements(int count)
        {
            var requirements = new ObservableCollection<Requirement>();
            for (var i = 1; i <= count; i++)
            {
                requirements.Add(new Requirement
                {
                    GlobalId = $"REQ-{i}",
                    Item = $"REQ-{i}",
                    Name = $"Requirement {i}",
                    Description = $"Description {i}"
                });
            }

            return requirements;
        }

        private sealed class InMemoryPersistenceService : IPersistenceService
        {
            private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);

            public void Save<T>(string key, T obj)
            {
                _store[key] = System.Text.Json.JsonSerializer.Serialize(obj);
            }

            public T? Load<T>(string key)
            {
                if (!_store.TryGetValue(key, out var json))
                {
                    return default;
                }

                return System.Text.Json.JsonSerializer.Deserialize<T>(json);
            }

            public bool Exists(string key) => _store.ContainsKey(key);

            public string[] GetAvailableBackups(string filePath) => Array.Empty<string>();

            public void RestoreFromBackup(string filePath, string backupPath)
            {
            }

            public bool CanUndo(string filePath) => false;

            public void UndoLastSave(string filePath)
            {
            }
        }
    }
}
