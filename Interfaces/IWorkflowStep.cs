using System.Threading.Tasks;

namespace TestCaseEditorApp.Interfaces
{
    public interface IWorkflowStep
    {
        Task EnterAsync();
        Task ExitAsync();
        Task<bool> ValidateAsync();
        Task SaveAsync();
        void Cancel();
    }
}