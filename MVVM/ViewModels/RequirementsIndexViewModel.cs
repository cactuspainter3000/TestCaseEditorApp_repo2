using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Data;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Small navigator VM for Requirements collection.
    /// - Keeps a SelectedRequirement (bound to the UI)
    /// - Exposes Prev/Next commands and a method to re-evaluate when the host's CurrentRequirement changes.
    /// - Provides a view (RequirementsView) that applies a natural numeric sort so items like
    ///   Decagon-REQ_RC-53 appear before Decagon-REQ_RC-54 (ascending numeric order).
    /// </summary>
    public class RequirementsIndexViewModel : ObservableObject
    {
        private readonly ObservableCollection<Requirement> _requirements;
        private readonly Func<Requirement?> _getCurrentRequirement;
        private readonly Action<Requirement?> _setCurrentRequirement;
        private readonly Action? _commitPendingEdits;
        private readonly ILogger<RequirementsIndexViewModel>? _logger;

        // Backing view for sorted display
        private readonly ListCollectionView _requirementsView;

        public RequirementsIndexViewModel(
            ObservableCollection<Requirement> requirements,
            Func<Requirement?> getCurrentRequirement,
            Action<Requirement?> setCurrentRequirement,
            Action? commitPendingEdits = null,
            ILogger<RequirementsIndexViewModel>? logger = null)
        {
            _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            _getCurrentRequirement = getCurrentRequirement ?? throw new ArgumentNullException(nameof(getCurrentRequirement));
            _setCurrentRequirement = setCurrentRequirement ?? throw new ArgumentNullException(nameof(setCurrentRequirement));
            _commitPendingEdits = commitPendingEdits;
            _logger = logger;

            // Commands - expose names the view expects (PreviousRequirementCommand / NextRequirementCommand)
            PreviousRequirementCommand = new RelayCommand(ExecutePrev, CanExecutePrev);
            NextRequirementCommand = new RelayCommand(ExecuteNext, CanExecuteNext);

            // Add search command (simple find-by-Item/Name)
            SearchCommand = new RelayCommand(ExecuteSearch, CanExecuteSearch);

            // Create a ListCollectionView over the backing collection so we can apply a custom sort.
            var view = CollectionViewSource.GetDefaultView(_requirements) as ListCollectionView;
            if (view == null)
            {
                // Fallback: wrap into a new ListCollectionView if necessary
                _requirementsView = new ListCollectionView(_requirements);
            }
            else
            {
                _requirementsView = view;
            }

            // Apply a custom comparer that does a natural numeric comparison (ascending numeric part).
            _requirementsView.CustomSort = new RequirementNaturalComparer();

            // Keep collection changes observed so command availability and position display update
            _requirements.CollectionChanged += (_, __) =>
            {
                // Refresh sorted view and notify UI
                try { _requirementsView.Refresh(); } catch { }
                NotifyCommands();
                OnPropertyChanged(nameof(RequirementPositionDisplay));
                OnPropertyChanged(nameof(SelectedRequirementIndex));
            };
        }

        // Exposed collection reference for the view to bind ItemsSource to (sorted view)
        public ICollectionView RequirementsView => _requirementsView;

        // Also expose the raw collection if other consumers need it
        public ObservableCollection<Requirement> Requirements => _requirements;

        // Exposed ICommand properties matching XAML bindings
        public IRelayCommand PreviousRequirementCommand { get; }
        public IRelayCommand NextRequirementCommand { get; }

        // Search support (optional)
        private string? _searchQuery;
        public string? SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery == value) return;
                _searchQuery = value;
                OnPropertyChanged(nameof(SearchQuery));
                SearchCommand?.NotifyCanExecuteChanged();
            }
        }
        public IRelayCommand? SearchCommand { get; }

        // SelectedRequirement is mirror of host's current requirement for binding convenience.
        // When set locally, we call back into the host via _setCurrentRequirement.
        private Requirement? _selectedRequirement;
        public Requirement? SelectedRequirement
        {
            get => _selectedRequirement ?? _getCurrentRequirement();
            set
            {
                if (Equals(_selectedRequirement, value)) return;
                _selectedRequirement = value;
                OnPropertyChanged(nameof(SelectedRequirement));

                // Commit pending edits in host before changing selection
                _commitPendingEdits?.Invoke();
                _setCurrentRequirement?.Invoke(value);

                _logger?.LogDebug("[NAV] SelectedRequirement set -> {RequirementItem}", value?.Item ?? "<null>");
                NotifyCommands();
                OnPropertyChanged(nameof(RequirementPositionDisplay));
                OnPropertyChanged(nameof(SelectedRequirementIndex));
            }
        }

        // Provide SelectedRequirementIndex relative to the sorted view so UI can bind SelectedIndex on ComboBox
        public int SelectedRequirementIndex
        {
            get
            {
                var cur = _getCurrentRequirement();
                if (cur == null) return -1;
                try
                {
                    return _requirementsView.IndexOf(cur);
                }
                catch
                {
                    // Fallback: index in raw collection
                    return _requirements.IndexOf(cur);
                }
            }
            set
            {
                // Map the view index back to an item and set it as selected
                if (value < 0 || value >= _requirementsView.Count) return;
                var candidate = _requirementsView.GetItemAt(value) as Requirement;
                if (candidate == null) return;
                if (Equals(_getCurrentRequirement(), candidate)) return;
                SelectedRequirement = candidate;
                OnPropertyChanged(nameof(SelectedRequirementIndex));
            }
        }

        // Readable position display for the UI (e.g. "1 / 21")
        public string RequirementPositionDisplay
        {
            get
            {
                var cur = _getCurrentRequirement();
                if (cur == null || _requirements.Count == 0) return "—";
                // Determine the visible (sorted) index if possible
                int idx;
                try { idx = _requirementsView.IndexOf(cur); }
                catch { idx = _requirements.IndexOf(cur); }
                if (idx < 0) return "—";
                return $"{idx + 1} / {_requirements.Count}";
            }
        }

        // Called by the host (MainViewModel) when it sets its CurrentRequirement so the navigator can sync.
        public void NotifyCurrentRequirementChanged()
        {
            // Clear any cached selection so getter pulls from host delegate,
            // then raise property changed so bindings update.
            _selectedRequirement = null;
            OnPropertyChanged(nameof(SelectedRequirement));

            // Refresh view (in case the current item moved or collection changed)
            try { _requirementsView.Refresh(); } catch { }

            OnPropertyChanged(nameof(RequirementPositionDisplay));
            OnPropertyChanged(nameof(SelectedRequirementIndex));
            NotifyCommands();

            _logger?.LogDebug("[NAV] NotifyCurrentRequirementChanged invoked. Current={Current}, Count={Count}",
                _getCurrentRequirement()?.Item ?? "<null>", _requirements.Count);
        }

        private void ExecutePrev()
        {
            var cur = _getCurrentRequirement();
            if (cur == null)
            {
                if (_requirementsView.Count > 0)
                {
                    _commitPendingEdits?.Invoke();
                    var first = _requirementsView.GetItemAt(0) as Requirement;
                    if (first != null) _setCurrentRequirement(first);
                    _logger?.LogDebug("[NAV] ExecutePrev -> wrapped to first item: {Item}", first?.Item ?? "<null>");

                    // Refresh navigator state after host change
                    NotifyCurrentRequirementChanged();
                    return;
                }
                return;
            }

            int idx;
            try { idx = _requirementsView.IndexOf(cur); }
            catch { idx = _requirements.IndexOf(cur); }

            if (idx > 0)
            {
                _commitPendingEdits?.Invoke();
                var prevItem = _requirementsView.GetItemAt(idx - 1) as Requirement;
                if (prevItem != null) _setCurrentRequirement(prevItem);
                _logger?.LogDebug("[NAV] ExecutePrev -> moved to view index {Index} ({Item})", idx - 1, prevItem?.Item ?? "<null>");

                // Refresh navigator state after host change
                NotifyCurrentRequirementChanged();
            }
        }

        private bool CanExecutePrev()
        {
            var cur = _getCurrentRequirement();
            if (cur == null) return _requirementsView.Count > 0 && false; // no current -> prev not meaningful
            int idx;
            try { idx = _requirementsView.IndexOf(cur); }
            catch { idx = _requirements.IndexOf(cur); }
            return idx > 0;
        }

        private void ExecuteNext()
        {
            var cur = _getCurrentRequirement();
            if (cur == null)
            {
                if (_requirementsView.Count > 0)
                {
                    _commitPendingEdits?.Invoke();
                    var first = _requirementsView.GetItemAt(0) as Requirement;
                    if (first != null) _setCurrentRequirement(first);
                    _logger?.LogDebug("[NAV] ExecuteNext -> wrapped to first item: {Item}", first?.Item ?? "<null>");

                    // Refresh navigator state after host change
                    NotifyCurrentRequirementChanged();
                    return;
                }
                return;
            }

            int idx;
            try { idx = _requirementsView.IndexOf(cur); }
            catch { idx = _requirements.IndexOf(cur); }

            if (idx >= 0 && idx < _requirementsView.Count - 1)
            {
                _commitPendingEdits?.Invoke();
                var nextItem = _requirementsView.GetItemAt(idx + 1) as Requirement;
                if (nextItem != null) _setCurrentRequirement(nextItem);
                _logger?.LogDebug("[NAV] ExecuteNext -> moved to view index {Index} ({Item})", idx + 1, nextItem?.Item ?? "<null>");

                // Refresh navigator state after host change
                NotifyCurrentRequirementChanged();
            }
        }

        private bool CanExecuteNext()
        {
            var cur = _getCurrentRequirement();
            if (cur == null) return _requirementsView.Count > 0 && false;
            int idx;
            try { idx = _requirementsView.IndexOf(cur); }
            catch { idx = _requirements.IndexOf(cur); }
            return idx >= 0 && idx < _requirementsView.Count - 1;
        }

        private void NotifyCommands()
        {
            PreviousRequirementCommand?.NotifyCanExecuteChanged();
            NextRequirementCommand?.NotifyCanExecuteChanged();
            SearchCommand?.NotifyCanExecuteChanged();
        }

        // -------------------------
        // Simple search implementation
        // -------------------------
        private void ExecuteSearch()
        {
            var q = _searchQuery?.Trim();
            if (string.IsNullOrEmpty(q)) return;

            // Try to match Item or Name (case-insensitive)
            var found = _requirements.FirstOrDefault(r =>
                (!string.IsNullOrEmpty(r.Item) && r.Item.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(r.Name) && r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));

            if (found != null)
            {
                SelectedRequirement = found;
                // Ensure index-based bindings update as well
                OnPropertyChanged(nameof(SelectedRequirementIndex));
            }
        }

        private bool CanExecuteSearch() => !string.IsNullOrWhiteSpace(_searchQuery) && _requirements.Count > 0;

        // ---- Custom comparer: natural numeric compare, ASCENDING on trailing number ----
        private class RequirementNaturalComparer : IComparer
        {
            private static readonly Regex _trailingNumberRegex = new Regex(@"^(.*?)(\d+)$", RegexOptions.Compiled);

            public int Compare(object x, object y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is not Requirement a) return -1;
                if (y is not Requirement b) return 1;

                // Prefer 'Item' then 'Name' as the canonical id string.
                var sa = (a.Item ?? a.Name ?? string.Empty).Trim();
                var sb = (b.Item ?? b.Name ?? string.Empty).Trim();

                // If identical strings, consider them equal
                if (string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase)) return 0;

                var ma = _trailingNumberRegex.Match(sa);
                var mb = _trailingNumberRegex.Match(sb);

                if (ma.Success && mb.Success)
                {
                    var prefixA = ma.Groups[1].Value;
                    var prefixB = mb.Groups[1].Value;
                    if (!string.Equals(prefixA, prefixB, StringComparison.OrdinalIgnoreCase))
                    {
                        // Compare prefixes alphabetically
                        return StringComparer.OrdinalIgnoreCase.Compare(prefixA, prefixB);
                    }

                    // Both prefixes equal — compare numeric suffix ascending so 53 comes before 54
                    if (long.TryParse(ma.Groups[2].Value, out var na) && long.TryParse(mb.Groups[2].Value, out var nb))
                    {
                        // Ascending numeric order
                        var numCompare = na.CompareTo(nb);
                        if (numCompare != 0) return numCompare;
                    }

                    // Fallback to full-string compare if numeric equal
                    return StringComparer.OrdinalIgnoreCase.Compare(sa, sb);
                }

                // If one has numeric suffix and other not, place numeric-suffixed after/before depending on prefix
                if (ma.Success && !mb.Success)
                {
                    var prefixA = ma.Groups[1].Value;
                    var prefixB = sb;
                    var cmp = StringComparer.OrdinalIgnoreCase.Compare(prefixA, prefixB);
                    if (cmp != 0) return cmp;
                    // If prefixes same, treat the numeric-suffixed as less (so similar entries cluster)
                    return -1;
                }
                if (!ma.Success && mb.Success)
                {
                    var prefixA = sa;
                    var prefixB = mb.Groups[1].Value;
                    var cmp = StringComparer.OrdinalIgnoreCase.Compare(prefixA, prefixB);
                    if (cmp != 0) return cmp;
                    return 1;
                }

                // No numeric suffixes — plain string compare
                return StringComparer.OrdinalIgnoreCase.Compare(sa, sb);
            }
        }
    }
}