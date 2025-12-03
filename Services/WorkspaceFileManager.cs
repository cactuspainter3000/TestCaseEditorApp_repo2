using System;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Small wrapper around the existing global WorkspaceService to centralize save/load calls.
    /// This keeps call sites simpler and allows future enhancements (temp-copy behavior,
    /// atomic saves, telemetry) to be implemented in one place.
    /// </summary>
    internal static class WorkspaceFileManager
    {
        public static void Save(string path, Workspace workspace)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            global::WorkspaceService.Save(path, workspace);
        }

        public static Workspace? Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
            return global::WorkspaceService.Load(path);
        }
    }
}
