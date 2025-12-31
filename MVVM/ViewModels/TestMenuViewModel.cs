using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Test ViewModel for hierarchical menu development and testing
    /// </summary>
    public partial class TestMenuViewModel : ObservableObject
    {
        private readonly ILogger<TestMenuViewModel> _logger;

        [ObservableProperty]
        private ObservableCollection<MenuHierarchyItem> testMenuItems = new();

        [ObservableProperty]
        private string testStatus = "Test Menu Ready";

        public TestMenuViewModel(ILogger<TestMenuViewModel> logger)
        {
            _logger = logger;
            InitializeTestData();
        }

        private void InitializeTestData()
        {
            TestMenuItems = new ObservableCollection<MenuHierarchyItem>
            {
                // Test Case Generator Section
                new MenuHierarchyItem
                {
                    Title = "Test Case Generator",
                    Level = 1,
                    IsExpandable = true,
                    IsExpanded = false,
                    Icon = "",
                    Children = new ObservableCollection<MenuHierarchyItem>
                    {
                        new MenuHierarchyItem
                        {
                            Title = "Project",
                            Level = 2,
                            IsExpandable = true,
                            IsExpanded = false,
                            Icon = "📁",
                            Badge = "",
                            Children = new ObservableCollection<MenuHierarchyItem>
                            {
                                new MenuHierarchyItem
                                {
                                    Title = "🆕 New Project",
                                    Level = 3,
                                    IsExpandable = false,
                                    Command = TestActionCommand
                                },
                                new MenuHierarchyItem
                                {
                                    Title = "📁 Open Project",
                                    Level = 3,
                                    IsExpandable = false,
                                    Command = TestActionCommand
                                },
                                new MenuHierarchyItem
                                {
                                    Title = "💾 Save Project",
                                    Level = 3,
                                    IsExpandable = false,
                                    Command = TestActionCommand
                                },
                                new MenuHierarchyItem
                                {
                                    Title = "📤 Unload Project",
                                    Level = 3,
                                    IsExpandable = false,
                                    Command = TestActionCommand
                                }
                            }
                        },
                        new MenuHierarchyItem
                        {
                            Title = "Requirements",
                            Level = 2,
                            IsExpandable = true,
                            IsExpanded = false,
                            Icon = "📋",
                            Badge = "",
                            Children = new ObservableCollection<MenuHierarchyItem>
                            {
                                new MenuHierarchyItem
                                {
                                    Title = "📥 Import Additional Requirements",
                                    Level = 3,
                                    IsExpandable = false,
                                    Command = TestActionCommand
                                },
                                new MenuHierarchyItem
                                {
                                    Title = "⚡ Analyze All Requirements",
                                    Level = 3,
                                    IsExpandable = false,
                                    Command = TestActionCommand
                                }
                            }
                        },
                        new MenuHierarchyItem
                        {
                            Title = "LLM Learning",
                            Level = 2,
                            IsExpandable = true,
                            IsExpanded = false,
                            Icon = "🤖",
                            Badge = "3",
                            Children = new ObservableCollection<MenuHierarchyItem>
                            {
                                new MenuHierarchyItem
                                {
                                    Title = "🔍 Analyze Unanalyzed",
                                    Level = 3,
                                    IsExpandable = false,
                                    Command = TestActionCommand
                                },
                                new MenuHierarchyItem
                                {
                                    Title = "🔄 Re-analyze Modified",
                                    Level = 3,
                                    IsExpandable = false,
                                    Command = TestActionCommand
                                }
                            }
                        },
                        new MenuHierarchyItem
                        {
                            Title = "Test Case Generator",
                            Level = 2,
                            IsExpandable = false,
                            Icon = "⚙️",
                            Command = TestActionCommand
                        }
                    }
                },
                
                // Test Flow Generator Section
                new MenuHierarchyItem
                {
                    Title = "Test Flow Generator",
                    Level = 1,
                    IsExpandable = true,
                    IsExpanded = false,
                    Icon = "",
                    Children = new ObservableCollection<MenuHierarchyItem>
                    {
                        new MenuHierarchyItem
                        {
                            Title = "Test Flow",
                            Level = 2,
                            IsExpandable = true,
                            IsExpanded = false,
                            Icon = "🔄",
                            Children = new ObservableCollection<MenuHierarchyItem>
                            {
                                new MenuHierarchyItem
                                {
                                    Title = "🆕 Create New Flow",
                                    Level = 3,
                                    IsExpandable = false,
                                    Command = TestActionCommand
                                },
                                new MenuHierarchyItem
                                {
                                    Title = "✅ Validate Flow",
                                    Level = 3,
                                    IsExpandable = false,
                                    Command = TestActionCommand
                                },
                                new MenuHierarchyItem
                                {
                                    Title = "📤 Export Flow",
                                    Level = 3,
                                    IsExpandable = false,
                                    Command = TestActionCommand
                                }
                            }
                        },
                        new MenuHierarchyItem
                        {
                            Title = "Testing",
                            Level = 2,
                            IsExpandable = true,
                            IsExpanded = false,
                            Icon = "🧪",
                            Children = new ObservableCollection<MenuHierarchyItem>
                            {
                                new MenuHierarchyItem
                                {
                                    Title = "🧪 Test Option 1",
                                    Level = 3,
                                    IsExpandable = false,
                                    Command = TestActionCommand
                                },
                                new MenuHierarchyItem
                                {
                                    Title = "🔬 Test Option 2",
                                    Level = 3,
                                    IsExpandable = false,
                                    Command = TestActionCommand
                                }
                            }
                        }
                    }
                }
            };

            _logger.LogInformation("Test menu data initialized with {Count} sections", TestMenuItems.Count);
        }

        [RelayCommand]
        private void TestAction(MenuHierarchyItem? item)
        {
            var itemTitle = item?.Title ?? "Unknown";
            TestStatus = $"Executed: {itemTitle}";
            _logger.LogInformation("Test action executed for: {ItemTitle}", itemTitle);
        }

        [RelayCommand]
        private void ResetTestData()
        {
            InitializeTestData();
            TestStatus = "Test data reset";
            _logger.LogInformation("Test menu data reset");
        }
    }
}