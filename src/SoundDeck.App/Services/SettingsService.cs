using Microsoft.UI.Dispatching;
using SoundDeck.Core.Settings;

namespace SoundDeck.App.Services;

/// <summary>設定の保持と保存。変更は少し待ってからまとめて保存する（スライダー操作のたびに書かないため）。</summary>
internal sealed class SettingsService
{
    private readonly SettingsStore _store = new(AppPaths.SettingsFile);
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly bool _readOnly;

    /// <param name="readOnly">true なら保存しない（見た目確認用の起動で、動いている SoundDeck の設定を書き換えないため）。</param>
    public SettingsService(DispatcherQueue dispatcher, bool readOnly = false)
    {
        _readOnly = readOnly;
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
        if (_readOnly)
        {
            return;
        }

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
