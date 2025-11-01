using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public class ClarifyingQuestionsViewModel : ObservableObject
    {
        private readonly IPersistenceService _persistence;
        private const string PersistenceKey = "clarifying_questions";

        public ClarifyingQuestionsViewModel(IPersistenceService persistence)
        {
            _persistence = persistence;

            var loaded = _persistence.Load<string[]>(PersistenceKey);
            if (loaded != null && loaded.Length > 0)
            {
                Questions = new ObservableCollection<string>(loaded);
            }
            else
            {
                Questions = new ObservableCollection<string>
                {
                    "CQ-001: What environment will this run in?",
                    "CQ-002: What browsers are supported?"
                };
            }

            Questions.CollectionChanged += Questions_CollectionChanged;

            AddQuestionCommand = new RelayCommand(() => Questions.Add("New clarifying question"));
            MarkResolvedCommand = new RelayCommand(() =>
            {
                if (SelectedQuestion != null)
                {
                    Questions.Remove(SelectedQuestion);
                    SelectedQuestion = null;
                }
            });
        }

        private void Questions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _persistence.Save(PersistenceKey, Questions.ToArray());
        }

        public ObservableCollection<string> Questions { get; private set; }

        private string? _selectedQuestion;
        public string? SelectedQuestion
        {
            get => _selectedQuestion;
            set => SetProperty(ref _selectedQuestion, value);
        }

        public ICommand AddQuestionCommand { get; }
        public ICommand MarkResolvedCommand { get; }
    }
}