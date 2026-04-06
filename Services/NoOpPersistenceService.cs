using System;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// No-op persistence service used for design-time / parameterless MainViewModel ctor.
    /// Provides a single Load<T>(string) implementation to avoid duplicate-signature conflicts.
    /// </summary>
    internal class NoOpPersistenceService : IPersistenceService
    {
        // Key-based save/load (generic)
        public void Save<T>(string key, T value) { /* no-op */ }
        public T? Load<T>(string key) => default;

        // Path-based workspace save/load used by MainViewModel
        public void Save(string path, Workspace workspace) { /* no-op */ }

        // Existence check
        public bool Exists(string path) => false;
        // Backup/Undo functionality (no-op)
        public string[] GetAvailableBackups(string filePath) => Array.Empty<string>();
        public void RestoreFromBackup(string filePath, string backupPath) { /* no-op */ }
        public bool CanUndo(string filePath) => false;
        public void UndoLastSave(string filePath) { /* no-op */ }    }
}