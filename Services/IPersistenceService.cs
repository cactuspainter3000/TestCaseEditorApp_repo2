namespace TestCaseEditorApp.Services
{
    public interface IPersistenceService
    {
        /// <summary>Save an object as JSON identified by key.</summary>
        void Save<T>(string key, T obj);

        /// <summary>Load an object by key. Returns default(T) if missing or on error.</summary>
        T? Load<T>(string key);

        // Backup/Undo functionality
        string[] GetAvailableBackups(string filePath);
        void RestoreFromBackup(string filePath, string backupPath);
        bool CanUndo(string filePath);
        void UndoLastSave(string filePath);

        /// <summary>Return true if a persisted entry exists for key.</summary>
        bool Exists(string key);
    }
}