using System.Windows.Controls.Primitives;

namespace TestCaseEditorApp.Controls
{
    // Simple global manager so opening one DropDownButton closes others.
    internal static class DropDownManager
    {
        private static Popup _openPopup;

        public static void RegisterOpenPopup(Popup popup)
        {
            if (_openPopup != null && _openPopup != popup)
            {
                _openPopup.IsOpen = false;
            }

            _openPopup = popup;
        }

        public static void UnregisterPopup(Popup popup)
        {
            if (_openPopup == popup) _openPopup = null;
        }

        public static void CloseOpenPopup()
        {
            if (_openPopup != null)
            {
                _openPopup.IsOpen = false;
                _openPopup = null;
            }
        }
    }
}