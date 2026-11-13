<p align="center">
  <img src="preview/appicon.png" alt="Blitznotiz" width="120">
</p>

<h1 align="center">Blitznotiz</h1>

<p align="center"><b>Speech to text at the press of a key — for Windows.</b><br>
Hold a key, speak, release — the text lands right at your cursor.</p>

<p align="center"><b>🇬🇧 English</b> · <a href="README.de.md">🇩🇪 Deutsch</a></p>

---

Blitznotiz is a standalone Windows app (.NET 9 / Avalonia 11), **inspired by
[Blitztext](https://github.com/cmagnussen/blitztext-app) by Christoph Magnussen** (macOS).
**No source code was copied** — it is an independent, from-scratch reimplementation of the same
idea, with one feature of its own on top: **Blitznotiz**.

> The app's interface is in German.

## What it does

Push-to-talk dictation in four styles. **Hold** the hotkey, speak, **release**:

| Hotkey | Mode | What happens |
|--------|------|--------------|
| `F9` | Verbatim       | your speech is transcribed 1:1 |
| `F6` | Improved       | the transcript is smoothed into clean writing |
| `F8` | Vent           | a frustrated message is rewritten into a calm one |
| `F7` | Emoji          | fitting emojis are added |
| `Esc`| Cancel         | discard the current recording |

Audio is transcribed (Whisper), optionally rewritten (GPT) and pasted **straight at your cursor**
into whatever app you're working in. Blitznotiz lives in the system tray — there is no main window.

<table>
  <tr>
    <td><img src="docs/screenshots/settings-workflows.png" width="420" alt="Workflows tab"></td>
    <td><img src="docs/screenshots/settings-verhalten.png" width="420" alt="Behaviour tab"></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/settings-blitznotiz.png" width="420" alt="Blitznotiz tab"></td>
    <td><img src="docs/screenshots/settings-current.png" width="420" alt="Access tab"></td>
  </tr>
</table>

## ⭐ Blitznotiz — the new feature

<img src="docs/screenshots/library.png" align="right" width="280" alt="Blitznotiz library">

The heart of this Windows build: **quick voice notes that file themselves.** A thought arrives,
you hold a key and speak — done. No window switching, no typing, no hunting for where to put it.

**Capture (`F11`, hold):** what you say is transcribed (verbatim or improved, your choice), copied
to the clipboard **and** appended as a checkbox bullet to today's note file. It is **not** pasted into
the active window — Blitznotiz only files it away, without interrupting you.

**Self-organising Markdown:** you pick a root folder; Blitznotiz sorts notes by year and month, one
readable file per day:

```
…\2026\2026-06\2026-06-07.md
```

Each note is a Markdown line with a timestamp — open is `- [ ]`, reused is `- [x]`:

```markdown
- [ ] 09:14 Idea for the meeting: open with a short example
- [x] 10:02 Point picked up from the phone call
```

Readable in any editor, portable, yours.

**Library (`F12`, toggle):** a slim, dockable panel (right or left). Today view, browse day↔day and
month↔month. The header shows the total, "reused" (green) and "open". **Clicking a note** copies it
back to the clipboard and turns the line pastel green — so you see at a glance what you've already
used. Two cleanup buttons ("Delete used" / "Delete all"), each with a confirmation prompt.

## Connecting

- **Your own OpenAI key** — the app talks to the OpenAI API directly.
- **Your own server** — an OpenAI-compatible proxy that checks an **app password** and keeps the real
  key on the server, so others can use it without a key of their own. For security this is only allowed
  over `https://` or private/Tailscale addresses — otherwise the app password would travel in the clear.

Secrets (key, app password) are stored encrypted in the Windows store (DPAPI), never in plain text.

## Local speech recognition (Parakeet Ultra)

Instead of cloud Whisper, speech can be recognized **offline on your own PC** with
[Parakeet Ultra](https://moondream.ai/blog/introducing-parakeet-redux-and-ultra) via
[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx): free, private, 25 European languages.

**Settings › Workflows › Speech recognition › "Lokal (Parakeet Ultra, offline)".**

- **Verbatim** (`F9`) and verbatim notes then need **no key and no internet**.
- **Improved, vent, emoji** still rewrite the text via OpenAI.
- Custom words, language and transcription model only apply to cloud recognition.

The model (~670 MB) is not bundled. Place it in
`%LOCALAPPDATA%\Blitztext\models\parakeet-ultra-int8\` (`encoder.int8.onnx`, `decoder.int8.onnx`,
`joiner.int8.onnx`, `tokens.txt`), e.g. from the int8 conversion
[leogaudin/sherpa-onnx-nemo-parakeet-ultra-int8](https://huggingface.co/leogaudin/sherpa-onnx-nemo-parakeet-ultra-int8)
(CC-BY-4.0). Recognition runs on the CPU, roughly 20× faster than real time on a current desktop.

## Install

Download **`Blitznotiz.exe`** from the **[Releases](../../releases)** page and run it.
A single file — **no installer, no separate .NET runtime required.**

On first launch the settings window opens: pick a connection type, enter your access, done.

## Build from source

Requires the .NET 9 SDK.

```powershell
# Development build
dotnet build Blitztext.csproj -c Debug

# Distribution build: one self-contained .exe
dotnet publish Blitztext.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist
```

## Differences from the original

Same idea, different platform: the original is a macOS/Swift menubar app; this is a complete Windows
rewrite in .NET, with **Blitznotiz** and an optional **server mode** added. The full list is in
**[DIFFERENCES.md](DIFFERENCES.md)** (Deutsch: [UNTERSCHIEDE.md](UNTERSCHIEDE.md)).

## Credits & inspiration

The idea, the design and the spark come from **[Blitztext](https://github.com/cmagnussen/blitztext-app)**,
the experimental macOS app by **Christoph Magnussen**. His project is explicitly meant to be built upon —
*"clone it, build it, read the code, change it, break it, fix it, and suggest improvements."* That is
exactly what happened here: a complete rebuild for Windows, with Blitznotiz as an idea of its own on top.
Thank you for the inspiration and the open invitation. 🙏

This repository builds on the Windows version
**[Blitznotiz by Daniel Müller](https://github.com/danielstefanmueller/Blitznotiz)** and extends it with,
among other things, local offline speech recognition.

## License

MIT — see [LICENSE](LICENSE). Third-party content (test audio, optional speech model) is listed in
[NOTICE.md](NOTICE.md). The name and icon "Blitznotiz" are independent. The name "Blitztext", its
logo and brand belong to Christoph Magnussen / Blackboat and are named here only descriptively, to credit
the original.

## A note on the app name

Everything you see in the app says **Blitznotiz**. Internally the project file, the namespace and the
build output (`Blitztext.exe`) still carry the development name "Blitztext"; the settings folder
`%APPDATA%\Blitztext`, the model folder `%LOCALAPPDATA%\Blitztext` and the autostart entry keep that name
on purpose, so existing installations keep their settings, key and model.
