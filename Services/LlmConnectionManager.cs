using System;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Simple, process-wide manager for LLM connection status.
    /// Call SetConnected(true/false) from your LLM integration (Ollama/OpenAI/LmStudio or TestCaseGenerator_CoreVM).
    /// Consumers subscribe to ConnectionChanged to be notified.
    /// </summary>
    public static class LlmConnectionManager
    {
        private static bool _isConnected;

        /// <summary>
        /// Current connection state (last reported).
        /// </summary>
        public static bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected == value) return;
                _isConnected = value;
                ConnectionChanged?.Invoke(_isConnected);
            }
        }

        /// <summary>
        /// Fired when connection state changes. Argument: true = connected, false = disconnected.
        /// </summary>
        public static event Action<bool>? ConnectionChanged;

        /// <summary>
        /// Call to report the current connection state from any LLM/service.
        /// </summary>
        public static void SetConnected(bool connected) => IsConnected = connected;
    }
}