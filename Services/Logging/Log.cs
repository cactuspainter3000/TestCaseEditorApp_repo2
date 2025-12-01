using System;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services.Logging
{
    internal static class Log
    {
        private static ILogger? GetLogger(string category = "App")
        {
            try
            {
                var sp = TestCaseEditorApp.App.ServiceProvider;
                if (sp == null) return null;
                var factory = sp.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
                return factory?.CreateLogger(category);
            }
            catch
            {
                return null;
            }
        }

        public static void Debug(string message)
        {
            var logger = GetLogger();
            if (logger != null)
            {
                logger.Log<string>(LogLevel.Debug, new EventId(0), message, null, (s, e) => s ?? string.Empty);
                return;
            }
            try { System.Diagnostics.Debug.WriteLine(message); } catch { }
        }

        public static void Info(string message)
        {
            var logger = GetLogger();
            if (logger != null)
            {
                logger.Log<string>(LogLevel.Information, new EventId(0), message, null, (s, e) => s ?? string.Empty);
                return;
            }
            try { System.Diagnostics.Debug.WriteLine(message); } catch { }
        }

        public static void Warn(string message)
        {
            var logger = GetLogger();
            if (logger != null)
            {
                logger.Log<string>(LogLevel.Warning, new EventId(0), message, null, (s, e) => s ?? string.Empty);
                return;
            }
            try { System.Diagnostics.Debug.WriteLine(message); } catch { }
        }

        public static void Error(Exception ex, string message)
        {
            var logger = GetLogger();
            if (logger != null)
            {
                logger.Log<string>(LogLevel.Error, new EventId(0), message + "\n" + ex.ToString(), null, (s, e) => s ?? string.Empty);
                return;
            }
            try { System.Diagnostics.Debug.WriteLine(message + " - " + ex.ToString()); } catch { }
        }
    }
}
