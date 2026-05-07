using System;
using Microsoft.Extensions.Logging;

// =====================================================================
// ENHANCED LOGGING SYSTEM - Pain Point Resolution
// =====================================================================
// PROBLEM SOLVED: Inconsistent method signatures causing build errors
// 
// OLD ISSUES:
// - Error() required both Exception AND message (forcing fake exceptions)
// - Only some log levels supported message-only calls
// - Repeated patterns not abstracted
//
// NEW FEATURES:
// - All log levels now support message-only calls: Debug, Info, Warn, Error, Critical
// - Exception+message variants still available for both Error and Critical
// - Convenience methods for common patterns: MethodEntry, MethodExit, Exception, ValidationFailure
// - Consistent API that prevents build errors
// =====================================================================

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
            var safeMessage = message ?? "Unknown error";
            var exceptionString = ex?.ToString() ?? "No exception details";
            
            if (logger != null)
            {
                logger.Log<string>(LogLevel.Error, new EventId(0), safeMessage + "\n" + exceptionString, null, (s, e) => s ?? string.Empty);
                return;
            }
            try { System.Diagnostics.Debug.WriteLine(safeMessage + " - " + exceptionString); } catch { }
        }

        /// <summary>
        /// Error logging without exception (for consistency with other log levels)
        /// </summary>
        public static void Error(string message)
        {
            var logger = GetLogger();
            if (logger != null)
            {
                logger.Log<string>(LogLevel.Error, new EventId(0), message, null, (s, e) => s ?? string.Empty);
                return;
            }
            try { System.Diagnostics.Debug.WriteLine(message); } catch { }
        }

        /// <summary>
        /// Critical error logging - highest severity
        /// </summary>
        public static void Critical(string message)
        {
            var logger = GetLogger();
            if (logger != null)
            {
                logger.Log<string>(LogLevel.Critical, new EventId(0), message, null, (s, e) => s ?? string.Empty);
                return;
            }
            try { System.Diagnostics.Debug.WriteLine($"[CRITICAL] {message}"); } catch { }
        }

        /// <summary>
        /// Critical error logging with exception
        /// </summary>
        public static void Critical(Exception ex, string message)
        {
            var logger = GetLogger();
            var safeMessage = message ?? "Critical error";
            var exceptionString = ex?.ToString() ?? "No exception details";
            
            if (logger != null)
            {
                logger.Log<string>(LogLevel.Critical, new EventId(0), safeMessage + "\n" + exceptionString, null, (s, e) => s ?? string.Empty);
                return;
            }
            try { System.Diagnostics.Debug.WriteLine($"[CRITICAL] {safeMessage} - {exceptionString}"); } catch { }
        }

        // ======== CONVENIENCE METHODS FOR COMMON PATTERNS ========

        /// <summary>
        /// Log method entry for debugging (common pattern in this codebase)
        /// </summary>
        public static void MethodEntry(string className, string methodName, string? parameters = null)
        {
            var message = $"[{className}] {methodName}({parameters ?? ""}) - Entry";
            Debug(message);
        }

        /// <summary>
        /// Log method exit for debugging
        /// </summary>
        public static void MethodExit(string className, string methodName, string? result = null)
        {
            var message = $"[{className}] {methodName} - Exit" + (result != null ? $" -> {result}" : "");
            Debug(message);
        }

        /// <summary>
        /// Log caught exception with automatic message formatting
        /// </summary>
        public static void Exception(Exception ex, string context)
        {
            Error(ex, $"{context}: {ex.Message}");
        }

        /// <summary>
        /// Log validation failure (common pattern for steel traps)
        /// </summary>
        public static void ValidationFailure(string context, string details)
        {
            Error($"🚨 VALIDATION FAILURE - {context}: {details}");
        }
    }
}
