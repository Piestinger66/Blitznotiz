<p align="center">
  <img src="preview/appicon.png" alt="Blitznotiz" width="120">
</p>

<h1 align="center">Blitznotiz</h1>

<p align="center"><b>Sprache zu Text auf Knopfdruck — für Windows.</b><br>
Taste halten, sprechen, loslassen — der Text landet direkt an deinem Cursor.</p>

<p align="center"><a href="README.md">🇬🇧 English</a> · <b>🇩🇪 Deutsch</b></p>

---

Blitznotiz ist eine eigenständige Windows-App (.NET 9 / Avalonia 11), **inspiriert von
[Blitztext](https://github.com/cmagnussen/blitztext-app) von Christoph Magnussen** (macOS).
Es wurde **kein Quellcode übernommen** — eine unabhängige Neu-Implementierung derselben Idee, von Grund
auf neu, mit einer eigenen Erweiterung obendrauf: **Blitznotiz**.

## Was es kann

Push-to-talk-Diktat in vier Stilen. Hotkey **halten**, sprechen, **loslassen**:

| Hotkey | Modus | Was passiert |
|--------|-------|--------------|
| `F9` | Wörtlich        | Gesprochenes wird 1:1 transkribiert |
| `F6` | Verbessert      | das Transkript wird zu sauberem Text geglättet |
| `F8` | Dampf ablassen  | eine Frust-Nachricht wird in eine ruhige Fassung umgeschrieben |
| `F7` | Emoji           | passende Emojis werden ergänzt |
| `Esc`| Abbruch         | laufende Aufnahme verwerfen |

Der Ton wird transkribiert (Whisper), optional umgeschrieben (GPT) und **direkt am Cursor** in die App
eingefügt, in der du gerade arbeitest. Blitznotiz lebt im Infobereich (Tray) — ohne Hauptfenster.

<table>
  <tr>
    <td><img src="docs/screenshots/settings-workflows.png" width="420" alt="Reiter Workflows"></td>
    <td><img src="docs/screenshots/settings-verhalten.png" width="420" alt="Reiter Verhalten"></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/settings-blitznotiz.png" width="420" alt="Reiter Blitznotiz"></td>
    <td><img src="docs/screenshots/settings-current.png" width="420" alt="Reiter Zugang"></td>
  </tr>
</table>

## ⭐ Blitznotiz — die neue Erweiterung

<img src="docs/screenshots/library.png" align="right" width="280" alt="Blitznotiz-Bibliothek">

Das Herzstück dieses Windows-Eigenbaus: **schnelle Sprach-Notizen, die sich von selbst ordnen.** Ein
Gedanke kommt, Taste halten, sprechen — abgelegt. Kein Fenster wechseln, kein Tippen, kein Suchen.

**Aufnehmen (`F11`, halten):** Das Gesprochene wird transkribiert (wörtlich oder verbessert, je nach
Einstellung), landet in der Zwischenablage **und** als Häkchen-Stichpunkt in deiner heutigen Notiz-Datei.
Es wird **nicht** ins aktive Fenster eingefügt — Blitznotiz legt nur ab, ohne zu stören.

**Selbst-ordnende Markdown-Ablage:** Du wählst einen Wurzel-Ordner; Blitznotiz sortiert nach Jahr und
Monat, eine gut lesbare Datei pro Tag:

```
…\2026\2026-06\2026-06-07.md
```

Jede Notiz ist eine Markdown-Zeile mit Uhrzeit — offen ist `- [ ]`, wiederverwendet `- [x]`:

```markdown
- [ ] 09:14 Idee fürs Meeting: mit einem kurzen Beispiel starten
- [x] 10:02 Stichpunkt aus dem Telefonat übernommen
```

Lesbar in jedem Editor, portabel, deins.

**Bibliothek (`F12`, auf/zu):** ein schmales, andockbares Panel (rechts oder links). Heute-Ansicht,
Blättern Tag↔Tag und Monat↔Monat. Im Kopf: Gesamtzahl, „wiederverwendet" (grün) und „offen". **Klick auf
eine Notiz** legt sie zurück in die Zwischenablage und färbt die Zeile pastellgrün — du siehst auf einen
Blick, was du schon genutzt hast. Zwei Aufräum-Knöpfe („Verwendete löschen" / „Alle löschen"), jeweils
mit Sicherheitsabfrage.

## Verbindungsarten

- **Eigener OpenAI-Schlüssel** — die App spricht direkt mit der OpenAI-API.
- **Eigener Server** — ein OpenAI-kompatibler Proxy, der ein **App-Passwort** prüft und den echten
  Schlüssel auf dem Server hält, sodass andere ohne eigenen Schlüssel mitnutzen können. Aus
  Sicherheitsgründen nur über `https://` oder private/Tailscale-Adressen erlaubt — sonst ginge das
  App-Passwort im Klartext über die Leitung.

Geheimnisse (Schlüssel, App-Passwort) liegen verschlüsselt im Windows-Speicher (DPAPI), nie im Klartext.

## Lokale Spracherkennung (Parakeet Ultra)

Statt Whisper in der Cloud kann die Sprache auch **offline auf dem eigenen PC** erkannt werden – mit
[Parakeet Ultra](https://moondream.ai/blog/introducing-parakeet-redux-and-ultra) über
[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx). Kostenlos, privat, Deutsch und 24 weitere
europäische Sprachen.

**Einstellungen › Workflows › Spracherkennung › „Lokal (Parakeet Ultra, offline)“.**

- **Wörtlich** (`F9`) und wörtliche Blitznotizen brauchen dann **keinen Schlüssel und kein Internet**.
- **Verbessert, Dampf ablassen, Emoji** schreiben den Text weiterhin über OpenAI um.
- „Eigene Wörter“, Sprache und Transkriptions-Modell gelten nur für die Cloud-Erkennung.

Das Modell (~670 MB) wird nicht mitgeliefert. Es gehört nach
`%LOCALAPPDATA%\Blitztext\models\parakeet-ultra-int8\` (Dateien `encoder.int8.onnx`,
`decoder.int8.onnx`, `joiner.int8.onnx`, `tokens.txt`), z. B. aus der int8-Umwandlung
[leogaudin/sherpa-onnx-nemo-parakeet-ultra-int8](https://huggingface.co/leogaudin/sherpa-onnx-nemo-parakeet-ultra-int8)
(CC-BY-4.0). Die Erkennung läuft auf dem Prozessor; auf einem aktuellen Desktop-PC etwa 20× schneller
als Echtzeit.

## Installation

Fertige Anwendung: unter **[Releases](../../releases)** die **`Blitznotiz.exe`** herunterladen und starten.
Eine einzige Datei — **kein Setup, kein separat installiertes .NET nötig.**

Beim ersten Start öffnet sich das Einstellungsfenster: Verbindungsart und Zugang eintragen, fertig.

## Aus dem Quellcode bauen

Voraussetzung: .NET 9 SDK.

```powershell
# Entwicklungs-Build
dotnet build Blitztext.csproj -c Debug

# Tests (der Erkennungstest läuft nur, wenn das lokale Modell installiert ist)
dotnet test tests/Blitznotiz.Tests

# Verteil-Build: eine einzige, eigenständige .exe
dotnet publish Blitztext.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist
```

## Unterschiede zum Original

Gleiche Idee, andere Plattform: Das Original ist eine macOS/Swift-Menubar-App, dies ist ein kompletter
Windows-Neubau in .NET, ergänzt um **Blitznotiz** und einen optionalen **Server-Modus**. Die ganze Liste
steht in **[UNTERSCHIEDE.md](UNTERSCHIEDE.md)** (English: [DIFFERENCES.md](DIFFERENCES.md)).

## Credit & Inspiration

Die Idee, das Design und der Funke kommen von **[Blitztext](https://github.com/cmagnussen/blitztext-app)**,
der experimentellen macOS-App von **Christoph Magnussen**. Sein Projekt ist ausdrücklich zum Weiterbauen
gedacht — *„clone it, build it, read the code, change it, break it, fix it, and suggest improvements."*
Genau das ist hier passiert: ein kompletter Neubau für Windows, mit Blitznotiz als eigener Idee obendrauf.
Danke für die Inspiration und die offene Einladung. 🙏

## Lizenz

MIT — siehe [LICENSE](LICENSE). Name und Icon „Blitznotiz" sind eigenständig. Der Name „Blitztext", dessen
Logo und Marke gehören Christoph Magnussen / Blackboat und werden hier nur beschreibend genannt, um das
Original zu würdigen.

## Hinweis zum App-Namen

Die aktuelle Programmdatei trägt aus der Entwicklung intern noch den Namen „Blitztext". Die öffentliche
Veröffentlichung heißt **Blitznotiz**; eine vollständige interne Umbenennung ist als nächster Schritt
vorgesehen.
