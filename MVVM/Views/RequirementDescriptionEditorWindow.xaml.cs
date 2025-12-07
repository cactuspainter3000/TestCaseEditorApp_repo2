using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class RequirementDescriptionEditorWindow : Window
    {
        public bool ShouldReAnalyze { get; private set; }

        public RequirementDescriptionEditorWindow()
        {
            InitializeComponent();
            ShouldReAnalyze = false;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            ShouldReAnalyze = false;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            ShouldReAnalyze = false;
            Close();
        }

        private async void ReAnalyze_Click(object sender, RoutedEventArgs e)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[EditorWindow] Re-Analyze clicked");
            
            // Get ViewModel and execute ReAnalyze command
            if (DataContext is TestCaseEditorApp.MVVM.ViewModels.TestCaseGenerator_AnalysisVM vm && vm.ReAnalyzeCommand != null)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[EditorWindow] Invoking ReAnalyzeCommand");
                await vm.ReAnalyzeCommand.ExecuteAsync(null);
            }
            else
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[EditorWindow] ERROR: Could not find ReAnalyzeCommand");
            }
        }

        private void InspectPrompt_Click(object sender, RoutedEventArgs e)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[EditorWindow] Inspect Prompt clicked");
            
            // Get ViewModel and inspect what's being sent to LLM
            if (DataContext is TestCaseEditorApp.MVVM.ViewModels.TestCaseGenerator_AnalysisVM vm)
            {
                try
                {
                    var promptInspection = vm.InspectAnalysisPrompt();
                    
                    // Write to a desktop file for easier viewing
                    var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    var fileName = $"LLM_Analysis_Prompt_Inspection_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
                    var filePath = Path.Combine(desktopPath, fileName);
                    
                    File.WriteAllText(filePath, promptInspection);
                    
                    // Show confirmation with option to open the file
                    var result = MessageBox.Show($"Prompt inspection saved to:\n{filePath}\n\nWould you like to open the file?", 
                                                 "LLM Analysis Prompt Inspection", 
                                                 MessageBoxButton.YesNo, 
                                                 MessageBoxImage.Information);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = filePath,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception openEx)
                        {
                            MessageBox.Show($"Failed to open file: {openEx.Message}", "Open Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error inspecting prompt: {ex.Message}", "Inspection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Could not access analysis view model.", "Inspection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
