using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TestCaseEditorApp.MVVM.Models
{
    public class GeneratedTestCase : ObservableObject
    {
        private readonly Action? _onChanged;
        private bool _isLoading = false;
        private string _title = string.Empty;
        private string _preconditions = string.Empty;
        private string _steps = string.Empty;
        private string _expectedResults = string.Empty;
        private bool _isSelected;

        public GeneratedTestCase(Action? onChanged = null)
        {
            _onChanged = onChanged;
        }

        public string Title
        {
            get => _title;
            set
            {
                if (SetProperty(ref _title, value))
                {
                    MarkDirty();
                }
            }
        }

        public string Preconditions
        {
            get => _preconditions;
            set
            {
                if (SetProperty(ref _preconditions, value))
                {
                    MarkDirty();
                }
            }
        }

        public string Steps
        {
            get => _steps;
            set
            {
                if (SetProperty(ref _steps, value))
                {
                    MarkDirty();
                }
            }
        }

        public string ExpectedResults
        {
            get => _expectedResults;
            set
            {
                if (SetProperty(ref _expectedResults, value))
                {
                    MarkDirty();
                }
            }
        }

        private void MarkDirty()
        {
            if (_onChanged != null && !_isLoading)
            {
                _onChanged();
                TestCaseEditorApp.Services.Logging.Log.Debug("[TestCase] Property changed - marked workspace dirty via delegate");
            }
        }

        /// <summary>
        /// Set properties without triggering dirty flag (used during loading).
        /// </summary>
        public void SetPropertiesForLoad(string title, string preconditions, string steps, string expectedResults)
        {
            _isLoading = true;
            try
            {
                Title = title;
                Preconditions = preconditions;
                Steps = steps;
                ExpectedResults = expectedResults;
            }
            finally
            {
                _isLoading = false;
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
