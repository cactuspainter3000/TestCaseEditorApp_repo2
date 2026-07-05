using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Mediators;

namespace TestCaseEditorApp.Services.DependencyInjection
{
    /// <summary>
    /// Training Data Validation domain services: human validation workflow for synthetic training examples.
    /// These services are SINGLETON for workflow state management.
    /// </summary>
    public static class TrainingDataValidationExtensions
    {
        public static IServiceCollection AddTrainingDataValidationServices(this IServiceCollection services)
        {
            // Training Data Validation Service (SINGLETON - maintains validation session state)
            services.AddSingleton<ITrainingDataValidationService, TrainingDataValidationService>();

            // Training Data Validation Mediator is registered in MediatorExtensions.AddDomainMediators()

            return services;
        }
    }
}
