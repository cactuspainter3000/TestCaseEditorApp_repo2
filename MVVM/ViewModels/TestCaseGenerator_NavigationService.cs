using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Concrete ITestCaseGenerator_Navigator that forwards to MainViewModel (single source of truth).
    /// Robust to MainViewModel.Requirements being reassigned: re-subscribes to the active collection.
    /// </summary>
    public class TestCaseGenerator_NavigationService : ObservableObject, ITestCaseGenerator_Navigator, IDisposable
    {
        private readonly MainViewModel _main;

        // track the collection we are subscribed to so we can rehook if it changes
        private ObservableCollection<Requirement>? _subscribedCollection;

        public TestCaseGenerator_NavigationService(MainViewModel main)
        {
            _main = main ?? throw new ArgumentNullException(nameof(main));

            // listen for MainViewModel property changes (notably the Requirements property)
            _main.PropertyChanged += Main_PropertyChanged;

            // hook the current collection instance (may be null initially)
            HookCollection(_main.Requirements);
        }

        private void HookCollection(ObservableCollection<Requirement>? coll)
        {
            if (ReferenceEquals(coll, _subscribedCollection)) return;

            // detach old
            if (_subscribedCollection != null)
            {
                try { _subscribedCollection.CollectionChanged -= SubscribedCollection_CollectionChanged; } catch { }
            }

            _subscribedCollection = coll;

            if (_subscribedCollection != null)
            {
                _subscribedCollection.CollectionChanged += SubscribedCollection_CollectionChanged;
            }

            // Notify bindings that the Requirements reference changed
            OnPropertyChanged(nameof(Requirements));
            OnPropertyChanged(nameof(RequirementPositionDisplay));

            // Ensure buttons enable state is updated
            TryNotifyHostCommands();
        }

        private void SubscribedCollection_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Forward change notification
            OnPropertyChanged(nameof(Requirements));
            OnPropertyChanged(nameof(RequirementPositionDisplay));

            // Update host commands' CanExecute
            TryNotifyHostCommands();
        }

        private void Main_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // If the Requirements collection reference changed, re-hook to the new instance
            if (e.PropertyName == nameof(MainViewModel.Requirements))
            {
                HookCollection(_main.Requirements);
                return;
            }

            // Forward relevant MainViewModel property changes to any listeners
            if (e.PropertyName == nameof(MainViewModel.CurrentRequirement))
            {
                OnPropertyChanged(nameof(CurrentRequirement));
                OnPropertyChanged(nameof(RequirementPositionDisplay));
                TryNotifyHostCommands();
            }
            else if (e.PropertyName == nameof(MainViewModel.WrapOnNextWithoutTestCase))
            {
                OnPropertyChanged(nameof(WrapOnNextWithoutTestCase));
            }
            else if (e.PropertyName == nameof(MainViewModel.RequirementPositionDisplay))
            {
                OnPropertyChanged(nameof(RequirementPositionDisplay));
            }
            else if (e.PropertyName == nameof(MainViewModel.IsLlmBusy))
            {
                OnPropertyChanged(nameof(IsLlmBusy));
            }
            else if (e.PropertyName == nameof(MainViewModel.IsBatchAnalyzing))
            {
                OnPropertyChanged(nameof(IsBatchAnalyzing));
            }
        }

        private void TryNotifyHostCommands()
        {
            try { (_main.PreviousRequirementCommand as IRelayCommand)?.NotifyCanExecuteChanged(); } catch { }
            try { (_main.NextRequirementCommand as IRelayCommand)?.NotifyCanExecuteChanged(); } catch { }
            try { (_main.NextWithoutTestCaseCommand as IRelayCommand)?.NotifyCanExecuteChanged(); } catch { }
        }

        // ITestCaseGenerator_Navigator implementation (forwards to MainViewModel)
        public ObservableCollection<Requirement> Requirements => _main.Requirements;

        public Requirement? CurrentRequirement
        {
            get => _main.CurrentRequirement;
            set
            {
                if (_main.CurrentRequirement != value)
                {
                    _main.CurrentRequirement = value;
                    OnPropertyChanged(nameof(CurrentRequirement));
                    OnPropertyChanged(nameof(RequirementPositionDisplay));
                    TryNotifyHostCommands();
                }
            }
        }

        public ICommand? NextRequirementCommand => _main.NextRequirementCommand;
        public ICommand? PreviousRequirementCommand => _main.PreviousRequirementCommand;
        public ICommand? NextWithoutTestCaseCommand => _main.NextWithoutTestCaseCommand;

        public string RequirementPositionDisplay => _main.RequirementPositionDisplay;

        public bool WrapOnNextWithoutTestCase
        {
            get => _main.WrapOnNextWithoutTestCase;
            set
            {
                if (_main.WrapOnNextWithoutTestCase != value)
                {
                    _main.WrapOnNextWithoutTestCase = value;
                    OnPropertyChanged(nameof(WrapOnNextWithoutTestCase));
                }
            }
        }

        public bool IsLlmBusy
        {
            get => _main.IsLlmBusy;
            set
            {
                if (_main.IsLlmBusy != value)
                {
                    _main.IsLlmBusy = value;
                    OnPropertyChanged(nameof(IsLlmBusy));
                }
            }
        }

        public bool IsBatchAnalyzing => _main.IsBatchAnalyzing;

        public void Dispose()
        {
            try { if (_subscribedCollection != null) _subscribedCollection.CollectionChanged -= SubscribedCollection_CollectionChanged; } catch { }
            try { _main.PropertyChanged -= Main_PropertyChanged; } catch { }
        }
    }
}