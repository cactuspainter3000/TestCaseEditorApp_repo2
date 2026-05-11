using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Views;

namespace TestCaseEditorApp.Services
{
    public class SettingsDialogService : ISettingsDialogService
    {
        private readonly IServiceProvider _serviceProvider;

        public SettingsDialogService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public bool ShowSettingsDialog(Window? owner = null, bool isRequired = false)
        {
            var vm = _serviceProvider.GetRequiredService<UserSettingsViewModel>();
            vm.IsRequired = isRequired;

            var dialog = new UserSettingsWindow
            {
                Owner = owner ?? Application.Current?.MainWindow,
                DataContext = vm
            };

            vm.RequestClose += (_, result) =>
            {
                dialog.DialogResult = result;
                dialog.Close();
            };

            var dialogResult = dialog.ShowDialog();
            return dialogResult == true;
        }

        public bool ShowSettingsDialog(Func<Task<(bool Success, List<string> Issues)>>? validationCallback, Window? owner = null, bool isRequired = false)
        {
            var vm = _serviceProvider.GetRequiredService<UserSettingsViewModel>();
            vm.IsRequired = isRequired;
            vm.ValidationCallback = validationCallback;

            var dialog = new UserSettingsWindow
            {
                Owner = owner ?? Application.Current?.MainWindow,
                DataContext = vm
            };

            vm.RequestClose += (_, result) =>
            {
                dialog.DialogResult = result;
                dialog.Close();
            };

            var dialogResult = dialog.ShowDialog();
            return dialogResult == true;
        }
    }
}
