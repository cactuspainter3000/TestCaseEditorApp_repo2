namespace TestCaseEditorApp.Services
{
    public interface IUserSettingsService
    {
        AppUserSettings LoadSettings();
        void SaveSettings(AppUserSettings settings);
        void ApplySettingsToEnvironment(AppUserSettings settings);
        bool HasMissingRequiredSettings();
    }
}
