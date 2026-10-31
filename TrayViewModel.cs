using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Blitztext;

/// <summary>
/// Tray-Präsenz + Diktat-Orchestrierung von Blitztext (auf Avalonia portiert).
/// Push-to-talk: Taste halten = aufnehmen, loslassen = transkribieren → optional umschreiben → einfügen.
/// Der Tastatur-Hook-Callback läuft auf dem UI-Thread; schwere Arbeit wird per Dispatcher nachgelagert.
/// </summary>
internal sealed class TrayViewModel : INotifyPropertyChanged, IDisposable
{
    private enum AppState { Idle, Recording, Processing }

    private readonly TrayAnimator _animator;
    private Settings _settings;
    private readonly KeyboardHook _hook;
    private readonly Recorder _recorder = new();
    // Werden bei RebuildHotkeyMap als ganze, neue Instanzen ausgetauscht (atomarer Referenz-Swap),
    // statt in-place geleert/befüllt – so sieht der Hook-Thread immer einen konsistenten Stand.
    private Dictionary<int, WorkflowKind> _map = new();
    private HashSet<int> _swallow = new();
    private int _noteVk = -1;      // Blitznotiz aufnehmen (Push-to-talk, legt nur ab)
    private int _libraryVk = -1;   // Bibliothek auf/zu (Toggle, kein Halten)

    private SettingsWindow? _settingsWindow;
    private LibraryWindow? _libraryWindow;
    private WindowIcon _trayIcon;
    private string _trayTooltip = AppBrand.Name;

    private AppState _state = AppState.Idle;
    private int _activeKey;
    private WorkflowKind _activeKind;
    private bool _activeIsNote;
    private IntPtr _targetWindow;
    private CancellationTokenSource? _cts;
    private int _session; // zählt jede neue Aufnahme; entwertet veraltete Verarbeitungs-Tasks

    public TrayViewModel()
    {
        Recorder.SweepOldTempFiles();
        _settings = Settings.Load();

        _animator = new TrayAnimator(SetTrayIcon, reduceMotion: false);
        _trayIcon = _animator.InitialIcon;

        OpenSettingsCommand = new RelayCommand(OpenSettings);
        QuitCommand = new RelayCommand(Quit);

        RebuildHotkeyMap();

        _hook = new KeyboardHook { SwallowKey = vk => _swallow.Contains(vk) };
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;

        Logger.Log($"Blitztext gestartet (Avalonia). Erkennung={_settings.TranscriptionEngine}, Transkription={_settings.TranscriptionModel}, AutoPaste={_settings.AutoPaste}.");
        PreloadLocalModel();

        // Erststart-Hilfe: ohne hinterlegten Zugang sähe der Nutzer beim Tastendruck nichts.
        if (!IsConfigured())
            Dispatcher.UIThread.Post(OpenSettings);
    }

    private bool IsConfigured()
    {
        // Lokal reicht das Modell für "wörtlich"; fürs Umschreiben meldet sich die Cloud-Prüfung beim Diktat.
        if (_settings.TranscriptionEngine == TranscriptionEngine.Local)
            return LocalModel.IsComplete(LocalModel.DefaultDir);
        if (_settings.Mode == ConnectionMode.Server)
            return !string.IsNullOrWhiteSpace(_settings.ServerUrl)
                && !string.IsNullOrWhiteSpace(SecretStore.Load(SecretStore.AppPassword));
        return !string.IsNullOrWhiteSpace(SecretStore.Load(SecretStore.OpenAiKey));
    }

    // ── Bindings ────────────────────────────────────────────────────────────────
    public WindowIcon TrayIcon { get => _trayIcon; private set { _trayIcon = value; OnPropertyChanged(nameof(TrayIcon)); } }
    public string TrayTooltip { get => _trayTooltip; private set { _trayTooltip = value; OnPropertyChanged(nameof(TrayTooltip)); } }
    public ICommand OpenSettingsCommand { get; }
    public ICommand QuitCommand { get; }

    private void SetTrayIcon(WindowIcon icon) => TrayIcon = icon;

    // ── Hotkeys ─────────────────────────────────────────────────────────────────
    private void RebuildHotkeyMap()
    {
        var map = new Dictionary<int, WorkflowKind>();
        var swallow = new HashSet<int>();
        int noteVk = -1, libraryVk = -1;

        void Add(string name, WorkflowKind kind)
        {
            if (KeyNames.ToVk(name) is int vk) { map[vk] = kind; swallow.Add(vk); }
        }

        Add(_settings.Hotkeys.Transcribe, WorkflowKind.Transcribe);
        Add(_settings.Hotkeys.Improve, WorkflowKind.Improve);
        Add(_settings.Hotkeys.Steam, WorkflowKind.Steam);
        Add(_settings.Hotkeys.Emoji, WorkflowKind.Emoji);

        // Blitznotiz nutzt denselben Aufnahme-Weg, aber den gewählten Modus (verbessert/wörtlich);
        // das Ergebnis wird nur abgelegt statt eingefügt (siehe StopAndProcessAsync).
        var noteKind = _settings.BlitzNoteImproved ? WorkflowKind.Improve : WorkflowKind.Transcribe;
        if (KeyNames.ToVk(_settings.Hotkeys.Note) is int nvk) { map[nvk] = noteKind; swallow.Add(nvk); noteVk = nvk; }
        if (KeyNames.ToVk(_settings.Hotkeys.Library) is int lvk) { swallow.Add(lvk); libraryVk = lvk; }

        // Atomar austauschen – der Tastatur-Hook liest stets eine vollständige Instanz.
        _map = map;
        _swallow = swallow;
        _noteVk = noteVk;
        _libraryVk = libraryVk;
    }

    // ── Tasten (Hook-Callback läuft auf dem UI-Thread) ──────────────────────────
    private void OnKeyPressed(int key)
    {
        if (key == KeyNames.VK_ESCAPE)
        {
            if (_state != AppState.Idle) Dispatcher.UIThread.Post(HandleEscape);
            return;
        }
        if (_state != AppState.Idle) return;
        if (key == _libraryVk) { Dispatcher.UIThread.Post(ToggleLibrary); return; }
        if (!_map.TryGetValue(key, out var kind)) return;

        _state = AppState.Recording;
        _session++;
        _activeKey = key;
        _activeKind = kind;
        _activeIsNote = key == _noteVk;
        _targetWindow = Paster.GetActiveWindow();
        int session = _session;
        UpdateUi();
        Dispatcher.UIThread.Post(() => StartCapture(session));
    }

    private void OnKeyReleased(int key)
    {
        if (_state == AppState.Recording && key == _activeKey)
        {
            _state = AppState.Processing;
            int session = _session;
            var kind = _activeKind;
            var isNote = _activeIsNote;
            var target = _targetWindow;
            UpdateUi();
            Dispatcher.UIThread.Post(() => _ = StopAndProcessAsync(session, kind, target, isNote));
        }
    }

    private void StartCapture(int session)
    {
        if (session != _session || _state != AppState.Recording) return;
        try
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _recorder.Start();
            Logger.Log($"Aufnahme gestartet (Modus {_activeKind}, Ziel-Fenster {_targetWindow}).");
            if (TranscriptionPlan.NeedsCloud(_settings, _activeKind)) WarmUpConnection();
        }
        catch (Exception ex)
        {
            if (session == _session) { _state = AppState.Idle; UpdateUi(); }
            Logger.Log("Mikrofon-Fehler: " + ex.Message);
            Toast.Show("Mikrofon-Fehler: " + ex.Message, ToastKind.Error);
        }
    }

    private void PreloadLocalModel()
    {
        if (_settings.TranscriptionEngine == TranscriptionEngine.Local && LocalModel.IsComplete(LocalModel.DefaultDir))
            LocalTranscriber.Preload(LocalModel.DefaultDir);
    }

    private void WarmUpConnection()
    {
        try
        {
            var (baseUrl, token, error) = ResolveConnection(_settings);
            if (error == null) new OpenAiClient(baseUrl, token).WarmUp();
        }
        catch { /* Vorwärmen ist optional */ }
    }

    private void HandleEscape()
    {
        _session++;
        _cts?.Cancel();
        if (_state == AppState.Recording) _recorder.Cancel();
        _state = AppState.Idle;
        UpdateUi();
    }

    private async Task StopAndProcessAsync(int mySession, WorkflowKind kind, IntPtr targetWindow, bool isNote)
    {
        var settings = _settings; // ein Diktat arbeitet konsistent gegen EINEN Settings-Stand (verhindert Misch-Zustand, falls währenddessen gespeichert wird)
        var sw = Stopwatch.StartNew();
        var token = _cts?.Token ?? CancellationToken.None;
        string? capturedPath = null;
        try
        {
            var (path, seconds) = await _recorder.StopAsync();
            capturedPath = path;
            if (IsStale(mySession, token)) return;
            var bytes = !string.IsNullOrEmpty(path) && File.Exists(path) ? new FileInfo(path).Length : 0;
            float peak = _recorder.MaxPeak;
            Logger.Log($"Aufnahme gestoppt: {seconds:0.00}s, {bytes} Bytes, Pegel={peak:0.000}, t={sw.ElapsedMilliseconds}ms.");

            if (string.IsNullOrEmpty(path) || seconds < 0.3)
            {
                Logger.Log("Verworfen: zu kurz/keine Datei.");
                Toast.Show(DiscardHint.For(DiscardReason.TooShort, peak), ToastKind.Info);
                return;
            }

            OpenAiClient? client = null;
            if (TranscriptionPlan.NeedsCloud(settings, kind))
            {
                var (baseUrl, tokenValue, configError) = ResolveConnection(settings);
                if (configError != null) { Logger.Log("Konfig: " + configError); Toast.Show(configError, ToastKind.Error); return; }
                client = new OpenAiClient(baseUrl, tokenValue);
            }

            var t0 = sw.ElapsedMilliseconds;
            string transcript;
            if (settings.TranscriptionEngine == TranscriptionEngine.Local)
            {
                transcript = await LocalTranscriber.TranscribeAsync(LocalModel.DefaultDir, path, token);
            }
            else
            {
                string? whisperPrompt = seconds >= 0.9 ? Workflows.TranscriptionPrompt(settings) : null;
                transcript = await client!.TranscribeAsync(path, settings.TranscriptionModel, whisperPrompt, settings.Language, token);
            }
            if (IsStale(mySession, token)) return;
            Logger.Log($"Transkription erhalten ({settings.TranscriptionEngine}): {transcript?.Trim().Length ?? 0} Zeichen, dauer={sw.ElapsedMilliseconds - t0}ms, t={sw.ElapsedMilliseconds}ms.");

            if (string.IsNullOrWhiteSpace(transcript) || IsLikelyArtifact(transcript, seconds))
            {
                Logger.Log("Verworfen: leer/Artefakt.");
                Toast.Show(DiscardHint.For(DiscardReason.NothingRecognized, peak), ToastKind.Info);
                return;
            }

            string result = transcript;
            if (Workflows.NeedsRewrite(kind))
            {
                double temperature = Workflows.RewriteTemperature(kind);
                string model = string.IsNullOrWhiteSpace(settings.RewriteModel) ? Workflows.FastEditModel : settings.RewriteModel.Trim();
                string systemPrompt = Workflows.BuildSystemPrompt(kind, settings);
                t0 = sw.ElapsedMilliseconds;
                result = await client!.ChatAsync(model, systemPrompt, transcript, temperature, token);
                if (IsStale(mySession, token)) return;
                Logger.Log($"Rewrite fertig: {result?.Trim().Length ?? 0} Zeichen, api={sw.ElapsedMilliseconds - t0}ms, t={sw.ElapsedMilliseconds}ms.");
            }

            if (string.IsNullOrWhiteSpace(result)) return;
            if (isNote)
                await DeliverBlitzNoteAsync(result.Trim(), mySession, settings);
            else
                await DeliverResultAsync(result.Trim(), targetWindow, mySession, settings);
        }
        catch (OperationCanceledException) { Logger.Log("Abgebrochen (Esc/Timeout)."); }
        catch (OpenAiException ex) { Logger.Log("OpenAI-Fehler: " + ex.Message); }
        catch (FileNotFoundException ex) when (settings.TranscriptionEngine == TranscriptionEngine.Local)
        {
            Logger.Log(ex.Message);
            Toast.Show("Lokales Sprachmodell fehlt – siehe Einstellungen › Workflows.", ToastKind.Error);
        }
        catch (Exception ex) { Logger.Log("Unerwarteter Fehler: " + ex.Message); }
        finally
        {
            if (mySession == _session) { _recorder.DeleteFile(); ResetIdle(); }
            else if (!string.IsNullOrEmpty(capturedPath)) { try { File.Delete(capturedPath); } catch { } }
        }
    }

    private async Task DeliverResultAsync(string text, IntPtr targetWindow, int session, Settings settings)
    {
        if (session != _session) return;
        if (!await Paster.TrySetClipboardAsync(text)) { Logger.Log("Zwischenablage fehlgeschlagen."); Toast.Show("Zwischenablage fehlgeschlagen.", ToastKind.Error); return; }
        Logger.Log($"Zwischenablage gesetzt ({text.Length} Zeichen), AutoPaste={settings.AutoPaste}");
        if (session != _session) { _ = Paster.ClearIfUnchangedAsync(text, TimeSpan.Zero); return; }
        if (!settings.AutoPaste) return;

        bool refocused = Paster.RefocusIfNeeded(targetWindow);
        await Task.Delay(60); // dem Zielfenster kurz Zeit geben, den Fokus zu übernehmen
        if (session != _session) return;
        IntPtr fg = Paster.GetActiveWindow();

        if (settings.StrictPasteTarget && targetWindow != IntPtr.Zero && fg != targetWindow)
        {
            Logger.Log($"Einfügen blockiert: Ziel={targetWindow}, refokussiert={refocused}, Vordergrund={fg}.");
            Toast.Show("Text liegt in der Zwischenablage – Einfügen ins Zielfenster wurde blockiert.", ToastKind.Info);
            return;
        }

        // Erst einfügen, wenn die Ablage frei ist und noch unseren Text enthält (Win+V-Verlauf & Co.).
        var waitSw = Stopwatch.StartNew();
        bool clipFree = await Paster.WaitForClipboardFreeAsync(TimeSpan.FromMilliseconds(500));
        bool clipOurs = clipFree && Paster.ClipboardContains(text);
        if (clipFree && !clipOurs)
        {
            clipOurs = await Paster.TrySetClipboardAsync(text);
            if (clipOurs) clipFree = await Paster.WaitForClipboardFreeAsync(TimeSpan.FromMilliseconds(500));
        }
        if (session != _session) return;

        string focus = Paster.DescribeFocus(), modifiers = Paster.HeldModifiers();
        uint sent = Paster.SendCtrlV();
        Logger.Log($"Einfügen-Diagnose: Fokus={focus}, gedrückte Sondertasten={modifiers}");
        Logger.Log($"Einfügen: Ziel={targetWindow}, refokussiert={refocused}, FensterBeimEinfügen={fg}, SendInput={sent}, " +
                   $"AblageFrei={clipFree}, AblageUnserText={clipOurs}, gewartet={waitSw.ElapsedMilliseconds}ms");
        if (sent != 4) return;

        if (settings.ClearClipboardAfterPaste && settings.ClipboardClearSeconds > 0)
            _ = Paster.ClearIfUnchangedAsync(text, TimeSpan.FromSeconds(settings.ClipboardClearSeconds));
    }

    // ── Blitznotiz: nur ablegen (Datei + Zwischenablage), KEIN Einfügen ins aktive Fenster ──
    private async Task DeliverBlitzNoteAsync(string text, int session, Settings settings)
    {
        if (session != _session) return;

        bool savedToFile = false;
        try { BlitzNoteStore.Append(settings, DateTime.Now, text); savedToFile = true; }
        catch (Exception ex) { Logger.Log("Blitznotiz speichern fehlgeschlagen: " + ex.Message); }

        bool clip = await Paster.TrySetClipboardAsync(text);

        if (savedToFile)
        {
            Toast.Show("Notiz abgelegt – liegt auch in der Zwischenablage.", ToastKind.Success);
            _libraryWindow?.Refresh();
        }
        else if (clip)
            Toast.Show("Notiz nicht gespeichert – liegt aber in der Zwischenablage.", ToastKind.Error);
        else
            Toast.Show("Notiz konnte nicht abgelegt werden.", ToastKind.Error);

        Logger.Log($"Blitznotiz: Datei={savedToFile}, Zwischenablage={clip}, {text.Length} Zeichen.");
    }

    private void ToggleLibrary()
    {
        if (_libraryWindow != null) { _libraryWindow.Close(); return; }
        var win = new LibraryWindow(_settings);
        _libraryWindow = win;
        win.Closed += (_, _) => { if (ReferenceEquals(_libraryWindow, win)) _libraryWindow = null; };
        win.Show();
    }

    private (string baseUrl, string token, string? error) ResolveConnection(Settings settings)
    {
        if (settings.Mode == ConnectionMode.Server)
        {
            if (string.IsNullOrWhiteSpace(settings.ServerUrl)) return ("", "", "Kein Serverlink hinterlegt.");
            var pw = SecretStore.Load(SecretStore.AppPassword);
            if (string.IsNullOrWhiteSpace(pw)) return ("", "", "Kein App-Passwort hinterlegt.");
            return (settings.ServerUrl, pw, null);
        }
        var key = SecretStore.Load(SecretStore.OpenAiKey);
        if (string.IsNullOrWhiteSpace(key)) return ("", "", "Kein OpenAI-Key hinterlegt.");
        return ("https://api.openai.com", key, null);
    }

    private static bool IsLikelyArtifact(string text, double seconds)
    {
        string trimmed = text.Trim();
        int letters = 0;
        foreach (var c in trimmed) if (char.IsLetter(c)) letters++;
        if (letters == 0) return true;
        int chars = trimmed.Length;
        int words = trimmed.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        if (seconds < 0.55 && (words >= 5 || chars >= 32)) return true;
        if (seconds < 0.8 && chars >= 56) return true;
        return false;
    }

    private void ResetIdle() { _state = AppState.Idle; UpdateUi(); }

    private void UpdateUi()
    {
        switch (_state)
        {
            case AppState.Recording:
                _animator.SetState(TrayVisual.Recording);
                TrayTooltip = $"{AppBrand.Name} – Aufnahme…";
                break;
            case AppState.Processing:
                _animator.SetState(TrayVisual.Processing);
                TrayTooltip = $"{AppBrand.Name} – verarbeite…";
                break;
            default:
                _animator.SetState(TrayVisual.Idle);
                TrayTooltip = AppBrand.Name;
                break;
        }
    }

    private bool IsStale(int session, CancellationToken token) => token.IsCancellationRequested || session != _session;

    // ── Menü ────────────────────────────────────────────────────────────────────
    private void OpenSettings()
    {
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        var win = new SettingsWindow(_settings);
        _settingsWindow = win;
        win.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (!win.Saved) return;
            _settings = Settings.Load();   // frische Werte (inkl. Geheimnisse separat über SecretStore)
            RebuildHotkeyMap();            // neue Hotkeys sofort aktiv, ohne Neustart
            PreloadLocalModel();           // bei Wechsel auf "lokal" schon vor dem ersten Diktat laden
            _libraryWindow?.Close();       // öffnet beim nächsten Hotkey mit neuem Ordner/neuer Seite
            Logger.Log($"Einstellungen übernommen. Transkription={_settings.TranscriptionModel}, " +
                       $"Hotkeys {_settings.Hotkeys.Transcribe}/{_settings.Hotkeys.Improve}/" +
                       $"{_settings.Hotkeys.Steam}/{_settings.Hotkeys.Emoji}, " +
                       $"Notiz {_settings.Hotkeys.Note}/Bibliothek {_settings.Hotkeys.Library}.");
        };
        win.Show();
    }

    private void Quit()
    {
        Logger.Flush();
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    public void Dispose()
    {
        try { _hook.Dispose(); } catch { }
        try { _recorder.Dispose(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _animator.Dispose();
        Logger.Flush();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
