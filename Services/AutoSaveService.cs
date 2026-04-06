using System;
using System.Windows.Threading;

namespace TestCaseEditorApp.Services
{
    public class AutoSaveService : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly Func<bool> _shouldSave;
        private readonly Action _saveAction;
        private readonly Action<string, int>? _transientStatus;

        public AutoSaveService(TimeSpan interval, Func<bool> shouldSave, Action saveAction, Action<string, int>? transientStatus = null)
        {
            _shouldSave = shouldSave ?? (() => false);
            _saveAction = saveAction ?? (() => { });
            _transientStatus = transientStatus;

            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += TimerOnTick;
        }

        private void TimerOnTick(object? sender, EventArgs e)
        {
            try
            {
                if (_shouldSave())
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug("[AutoSave] Saving workspace...");
                    _saveAction();
                    _transientStatus?.Invoke($"Auto-saved at {DateTime.Now:HH:mm}", 2);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AutoSave] Failed: {ex.Message}");
            }
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        public void Dispose()
        {
            try { _timer.Stop(); } catch { }
        }
    }
}
