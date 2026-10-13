using Microsoft.UI.Dispatching;
using SoundDeck.Core.Settings;

namespace SoundDeck.App.Services;

/// <summary>設定の保持と保存。変更は少し待ってからまとめて保存する（スライダー操作のたびに書かないため）。</summary>
internal sealed class SettingsService
{
    private readonly SettingsStore _store = new(AppPaths.SettingsFile);
    private readonly DispatcherQueueTimer _saveTimer;

    public SettingsService(DispatcherQueue dispatcher)
    {
        Current = _store.Load();
        _saveTimer = dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(800);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => SaveNow();
    }

    /// <summary>設定画面などで変更があった（ホットキーの再登録などに使う）。</summary>
    public event EventHandler? Changed;

    public AppSettings Current { get; }

    public void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>保存し、変更を通知する。</summary>
    public void NotifyChanged()
    {
        ScheduleSave();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            _store.Save(Current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("設定の保存に失敗", ex);
        }
    }
}
