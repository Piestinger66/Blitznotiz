using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace Blitztext;

public partial class SettingsWindow : Window
{
    private readonly Settings _initial;

    // Vom Aufrufer (TrayViewModel) abgefragt: wurde gespeichert? Dann Einstellungen neu laden.
    public bool Saved { get; private set; }

    // Steuerelemente (manueller XAML-Loader → Zugriff über FindControl).
    private readonly TextBlock _message;
    private readonly RadioButton _modeOwnKey;
    private readonly RadioButton _modeServer;
    private readonly TextBox _openAiKeyBox;
    private readonly TextBox _serverUrlBox;
    private readonly TextBox _appPasswordBox;
    private readonly ComboBox _engineBox;
    private readonly TextBlock _engineInfo;
    private readonly ComboBox _transcriptionModelBox;
    private readonly ComboBox _rewriteModelBox;
    private readonly ComboBox _languageBox;
    private readonly ComboBox _toneBox;
    private readonly ComboBox _emojiDensityBox;
    private readonly TextBox _customTermsBox;
    private readonly ComboBox _hkTranscribe;
    private readonly ComboBox _hkImprove;
    private readonly ComboBox _hkSteam;
    private readonly ComboBox _hkEmoji;
    private readonly CheckBox _autoPasteBox;
    private readonly CheckBox _strictPasteBox;
    private readonly CheckBox _clearClipboardBox;
    private readonly NumericUpDown _clearSecondsBox;
    private readonly CheckBox _launchAtLoginBox;
    private readonly TextBox _blitzNoteRootBox;
    private readonly CheckBox _noteImprovedBox;
    private readonly ComboBox _panelSideBox;
    private readonly ComboBox _hkNote;
    private readonly ComboBox _hkLibrary;

    // Parameterloser Konstruktor für den XAML-Designer; Laufzeit nutzt den mit Settings.
    public SettingsWindow() : this(Settings.Load()) { }

    public SettingsWindow(Settings current)
    {
        _initial = current;
        InitializeComponent();

        _message = this.FindControl<TextBlock>("MessageText")!;
        _modeOwnKey = this.FindControl<RadioButton>("ModeOwnKey")!;
        _modeServer = this.FindControl<RadioButton>("ModeServer")!;
        _openAiKeyBox = this.FindControl<TextBox>("OpenAiKeyBox")!;
        _serverUrlBox = this.FindControl<TextBox>("ServerUrlBox")!;
        _appPasswordBox = this.FindControl<TextBox>("AppPasswordBox")!;
        _engineBox = this.FindControl<ComboBox>("EngineBox")!;
        _engineInfo = this.FindControl<TextBlock>("EngineInfoText")!;
        _transcriptionModelBox = this.FindControl<ComboBox>("TranscriptionModelBox")!;
        _rewriteModelBox = this.FindControl<ComboBox>("RewriteModelBox")!;
        _languageBox = this.FindControl<ComboBox>("LanguageBox")!;
        _toneBox = this.FindControl<ComboBox>("ToneBox")!;
        _emojiDensityBox = this.FindControl<ComboBox>("EmojiDensityBox")!;
        _customTermsBox = this.FindControl<TextBox>("CustomTermsBox")!;
        _hkTranscribe = this.FindControl<ComboBox>("HotkeyTranscribe")!;
        _hkImprove = this.FindControl<ComboBox>("HotkeyImprove")!;
        _hkSteam = this.FindControl<ComboBox>("HotkeySteam")!;
        _hkEmoji = this.FindControl<ComboBox>("HotkeyEmoji")!;
        _autoPasteBox = this.FindControl<CheckBox>("AutoPasteBox")!;
        _strictPasteBox = this.FindControl<CheckBox>("StrictPasteBox")!;
        _clearClipboardBox = this.FindControl<CheckBox>("ClearClipboardBox")!;
        _clearSecondsBox = this.FindControl<NumericUpDown>("ClearSecondsBox")!;
        _launchAtLoginBox = this.FindControl<CheckBox>("LaunchAtLoginBox")!;
        _blitzNoteRootBox = this.FindControl<TextBox>("BlitzNoteRootBox")!;
        _noteImprovedBox = this.FindControl<CheckBox>("NoteImprovedBox")!;
        _panelSideBox = this.FindControl<ComboBox>("PanelSideBox")!;
        _hkNote = this.FindControl<ComboBox>("HotkeyNote")!;
        _hkLibrary = this.FindControl<ComboBox>("HotkeyLibrary")!;

        LoadValues();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void LoadValues()
    {
        _modeOwnKey.IsChecked = _initial.Mode == ConnectionMode.OwnKey;
        _modeServer.IsChecked = _initial.Mode == ConnectionMode.Server;

        // Geheimnisse nie im Klartext anzeigen – nur per Wasserzeichen verraten, ob bereits hinterlegt.
        _openAiKeyBox.Watermark = SecretStore.Load(SecretStore.OpenAiKey).Length > 0
            ? "•••••• (hinterlegt — leer lassen zum Behalten)" : "sk-…";
        _appPasswordBox.Watermark = SecretStore.Load(SecretStore.AppPassword).Length > 0
            ? "•••••• (hinterlegt — leer lassen zum Behalten)" : "App-Passwort";

        _serverUrlBox.Text = _initial.ServerUrl;
        _customTermsBox.Text = _initial.CustomTerms;

        SelectByTag(_engineBox, _initial.TranscriptionEngine.ToString(), fallbackTag: nameof(TranscriptionEngine.Cloud));
        _engineBox.SelectionChanged += (_, _) => UpdateEngineInfo();
        UpdateEngineInfo();

        SelectByContent(_transcriptionModelBox, string.IsNullOrWhiteSpace(_initial.TranscriptionModel)
            ? Workflows.FallbackTranscriptionModel : _initial.TranscriptionModel);
        SelectByContent(_rewriteModelBox, string.IsNullOrWhiteSpace(_initial.RewriteModel)
            ? Workflows.FastEditModel : _initial.RewriteModel);

        SelectByTag(_languageBox, _initial.Language ?? "", fallbackTag: "");
        SelectByTag(_toneBox, _initial.Tone ?? "neutral", fallbackTag: "neutral");
        SelectByTag(_emojiDensityBox, _initial.EmojiDensity ?? "mittel", fallbackTag: "mittel");

        InitHotkey(_hkTranscribe, _initial.Hotkeys.Transcribe);
        InitHotkey(_hkImprove, _initial.Hotkeys.Improve);
        InitHotkey(_hkSteam, _initial.Hotkeys.Steam);
        InitHotkey(_hkEmoji, _initial.Hotkeys.Emoji);

        _autoPasteBox.IsChecked = _initial.AutoPaste;
        _strictPasteBox.IsChecked = _initial.StrictPasteTarget;
        _clearClipboardBox.IsChecked = _initial.ClearClipboardAfterPaste;
        _clearSecondsBox.Value = _initial.ClipboardClearSeconds;
        _launchAtLoginBox.IsChecked = _initial.LaunchAtLogin;

        _blitzNoteRootBox.Text = _initial.BlitzNoteRoot;
        _noteImprovedBox.IsChecked = _initial.BlitzNoteImproved;
        SelectByTag(_panelSideBox, _initial.BlitzNotePanelSide ?? "right", fallbackTag: "right");
        InitHotkey(_hkNote, _initial.Hotkeys.Note);
        InitHotkey(_hkLibrary, _initial.Hotkeys.Library);
    }

    private bool IsLocalSelected => TagOf(_engineBox) == nameof(TranscriptionEngine.Local);

    private void UpdateEngineInfo()
    {
        if (!IsLocalSelected)
        {
            _engineInfo.Text = "Sprache wird an OpenAI (bzw. deinen Server) geschickt und dort erkannt.";
            return;
        }
        _engineInfo.Text = LocalModel.IsComplete(LocalModel.DefaultDir)
            ? "✓ Modell gefunden. Erkennung läuft offline auf diesem PC, kostenlos. Umschreiben (verbessert, Dampf, Emoji) nutzt weiter OpenAI."
            : "✗ Modell nicht gefunden in " + LocalModel.DefaultDir + " – siehe README.";
    }

    private async void OnBrowseRoot(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Ablage-Ordner für Blitznotizen wählen",
            AllowMultiple = false,
        });
        if (folders.Count > 0)
        {
            var path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) _blitzNoteRootBox.Text = path;
            else ShowMessage("Dieser Ordner ist kein lokaler Pfad (z.B. OneDrive/Cloud) und kann nicht genutzt werden.");
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var t = ContentOf(_hkTranscribe);
        var i = ContentOf(_hkImprove);
        var st = ContentOf(_hkSteam);
        var em = ContentOf(_hkEmoji);
        var note = ContentOf(_hkNote);
        var lib = ContentOf(_hkLibrary);

        var keys = new[] { t, i, st, em, note, lib };
        if (keys.Any(string.IsNullOrWhiteSpace))
        {
            ShowMessage("Bitte für jeden Hotkey eine Taste wählen.");
            return;
        }
        if (keys.Distinct().Count() != keys.Length)
        {
            ShowMessage("Jede Hotkey-Taste darf nur einmal vergeben sein.");
            return;
        }

        bool isServer = _modeServer.IsChecked == true;
        var serverUrl = _serverUrlBox.Text?.Trim() ?? "";
        if (isServer && !IsServerUrlAcceptable(serverUrl))
        {
            ShowMessage("Serverlink muss mit https:// beginnen oder eine private/Tailscale-Adresse sein – sonst ginge das App-Passwort im Klartext über die Leitung.");
            return;
        }

        if (IsLocalSelected && !LocalModel.IsComplete(LocalModel.DefaultDir))
        {
            ShowMessage("Lokale Erkennung gewählt, aber das Modell fehlt. Bitte Cloud wählen oder das Modell installieren.");
            return;
        }

        var s = new Settings
        {
            TranscriptionEngine = IsLocalSelected ? TranscriptionEngine.Local : TranscriptionEngine.Cloud,
            Mode = isServer ? ConnectionMode.Server : ConnectionMode.OwnKey,
            ServerUrl = serverUrl,
            Language = TagOf(_languageBox) ?? "",
            Tone = TagOf(_toneBox) ?? "neutral",
            EmojiDensity = TagOf(_emojiDensityBox) ?? "mittel",
            TranscriptionModel = ContentOf(_transcriptionModelBox) ?? Workflows.FallbackTranscriptionModel,
            RewriteModel = ContentOf(_rewriteModelBox) ?? Workflows.FastEditModel,
            CustomTerms = _customTermsBox.Text?.Trim() ?? "",
            // Felder ohne eigenes Bedienelement erhalten bleiben:
            ImproveSystemPrompt = _initial.ImproveSystemPrompt,
            SteamSystemPrompt = _initial.SteamSystemPrompt,
            AutoPaste = _autoPasteBox.IsChecked == true,
            StrictPasteTarget = _strictPasteBox.IsChecked == true,
            ClearClipboardAfterPaste = _clearClipboardBox.IsChecked == true,
            ClipboardClearSeconds = (int)(_clearSecondsBox.Value ?? 20m),
            LaunchAtLogin = _launchAtLoginBox.IsChecked == true,
            BlitzNoteRoot = _blitzNoteRootBox.Text?.Trim() ?? "",
            BlitzNotePanelSide = TagOf(_panelSideBox) ?? "right",
            BlitzNoteImproved = _noteImprovedBox.IsChecked == true,
            Hotkeys = new HotkeyConfig { Transcribe = t!, Improve = i!, Steam = st!, Emoji = em!, Note = note!, Library = lib! }
        };

        s.Save();

        // Geheimnisse: nur überschreiben, wenn der Nutzer wirklich etwas eingegeben hat.
        var newKey = _openAiKeyBox.Text?.Trim() ?? "";
        if (newKey.Length > 0) SecretStore.Save(SecretStore.OpenAiKey, newKey);
        var newPw = _appPasswordBox.Text?.Trim() ?? "";
        if (newPw.Length > 0) SecretStore.Save(SecretStore.AppPassword, newPw);

        Autostart.Apply(s.LaunchAtLogin);

        Saved = true;
        Close();
    }

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _message.IsVisible = true;
    }

    // ── Serverlink-Sicherheit ─────────────────────────────────────────────────────
    // https überall erlaubt; http NUR auf eindeutig verschlüsselten/privaten Zielen
    // (Loopback, privates LAN, Tailscale 100.64.0.0/10 bzw. *.ts.net). Sonst läge das
    // App-Passwort im Klartext auf der Leitung.
    private static bool IsServerUrlAcceptable(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme == Uri.UriSchemeHttps) return true;
        if (u.Scheme != Uri.UriSchemeHttp) return false;
        if (u.IsLoopback) return true;
        if (u.Host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(u.Host, out var ip) && IsPrivateOrCgnat(ip);
    }

    private static bool IsPrivateOrCgnat(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false; // IPv6: nur Loopback (oben) gilt
        var b = ip.GetAddressBytes();
        if (b[0] == 10) return true;                                  // 10.0.0.0/8
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;     // 172.16.0.0/12
        if (b[0] == 192 && b[1] == 168) return true;                  // 192.168.0.0/16
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;    // 100.64.0.0/10 (CGNAT/Tailscale)
        return false;
    }

    // ── kleine Helfer ────────────────────────────────────────────────────────────
    private static string? ContentOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Content as string;
    private static string? TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    private static void SelectByContent(ComboBox box, string value)
    {
        foreach (var it in box.Items)
            if (it is ComboBoxItem ci && (ci.Content as string) == value) { box.SelectedItem = ci; return; }
        var added = new ComboBoxItem { Content = value };
        box.Items.Add(added);
        box.SelectedItem = added;
    }

    private static void SelectByTag(ComboBox box, string value, string fallbackTag)
    {
        foreach (var it in box.Items)
            if (it is ComboBoxItem ci && (ci.Tag as string ?? "") == value) { box.SelectedItem = ci; return; }
        // Gesetzter, aber nicht gelisteter Wert (z.B. manuell „fr"): behalten statt beim Speichern still überschreiben.
        if (!string.IsNullOrEmpty(value))
        {
            var added = new ComboBoxItem { Content = value, Tag = value };
            box.Items.Add(added);
            box.SelectedItem = added;
            return;
        }
        // Leerer/fehlender Wert → sinnvoller Standard statt blind Index 0.
        foreach (var it in box.Items)
            if (it is ComboBoxItem ci && (ci.Tag as string ?? "") == fallbackTag) { box.SelectedItem = ci; return; }
        if (box.ItemCount > 0) box.SelectedIndex = 0;
    }

    private static void InitHotkey(ComboBox box, string current)
    {
        var names = new List<string>(KeyNames.Selectable);
        if (!string.IsNullOrWhiteSpace(current) && !names.Contains(current)) names.Add(current);
        foreach (var name in names) box.Items.Add(new ComboBoxItem { Content = name });
        SelectByContent(box, current);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
