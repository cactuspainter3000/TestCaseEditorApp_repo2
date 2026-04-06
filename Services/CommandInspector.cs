using System;
using System.Reflection;

namespace TestCaseEditorApp.Services
{
    internal static class CommandInspector
    {
        public static void LogCommandPresence(object? vm, params string[] commandNames)
        {
            try
            {
                Type? vmType = vm?.GetType();
                foreach (var name in commandNames)
                {
                    var prop = vmType?.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                    object? val = null;
                    if (prop != null && vm != null)
                    {
                        val = prop.GetValue(vm);
                    }
                    bool isIcmd = val is System.Windows.Input.ICommand;
                    var valueType = val?.GetType()?.FullName ?? "null";
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[CMD CHECK] {name}: propExists={prop != null}, valueType={valueType}, isICommand={isIcmd}");
                    if (isIcmd)
                    {
                        var cmd = val as System.Windows.Input.ICommand;
                        if (cmd != null)
                        {
                            bool can = true;
                            try { can = cmd.CanExecute(null); } catch (Exception ex) { TestCaseEditorApp.Services.Logging.Log.Debug($"[CMD CHECK] {name}.CanExecute threw: {ex}"); }
                            TestCaseEditorApp.Services.Logging.Log.Debug($"[CMD CHECK] {name}.CanExecute(null) = {can}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[CMD CHECK] Exception: " + ex);
            }
        }
    }
}
