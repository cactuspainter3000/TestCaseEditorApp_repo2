using System;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services
{
    public interface IModalService
    {
        Task ShowMessageAsync(string title, string message);
        Task<bool> ShowConfirmationAsync(string title, string message);
        void ShowModal(object content, string title = "Modal");
        void CloseModal();
    }

    public class StubModalService : IModalService
    {
        public Task ShowMessageAsync(string title, string message)
        {
            // Just log to console instead of showing modal
            System.Diagnostics.Debug.WriteLine($"Modal: {title} - {message}");
            return Task.CompletedTask;
        }

        public Task<bool> ShowConfirmationAsync(string title, string message)
        {
            // Always return true for confirmations
            System.Diagnostics.Debug.WriteLine($"Confirmation Modal: {title} - {message} (auto-confirmed)");
            return Task.FromResult(true);
        }

        public void ShowModal(object content, string title = "Modal")
        {
            System.Diagnostics.Debug.WriteLine($"ShowModal: {title}");
        }

        public void CloseModal()
        {
            System.Diagnostics.Debug.WriteLine("CloseModal called");
        }
    }
}