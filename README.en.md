# WordCatch

[简体中文](README.md) · English

A text lookup assistant for Windows 10/11 x64. Press **Ctrl + Alt + D** to read selected text from the current app. If selection capture is unavailable, WordCatch recognizes the word under the pointer. Clipboard text is looked up alongside it in a separate result card.

## Features

- Chinese and English interfaces, with an immediately applied, saved language choice under **Settings & About**. The installer also offers both languages.
- **Ctrl + Shift + Alt + D** selects a screen region for multiline OCR. Release to recognize; press Esc or right-click to cancel.
- Offline English–Chinese definitions, with optional Azure Translator fallback for sentences and missing entries.
- Separate cards identify selected text, screen OCR and clipboard sources. Edit the original text and press Enter to look up again.
- Missing compound entries are split at hyphens, underscores or camel-case boundaries and explained word by word. Complete dictionary entries take precedence.
- Local personal phrase dictionaries are supported. Personal databases are never included in the installer.
- Optional startup in the notification area when you sign in to Windows, off by default.
- New queries cancel previous cloud requests, preventing old results from replacing the current query.

![WordCatch interface](docs/images/main-window.png)

## Install and use

Download the **[v0.7.0 installer](https://github.com/tianqistudio/wordcatch/releases/tag/v0.7.0)**. It includes the runtime, OCR models and dictionary; no separate Python or .NET installation is required.

1. Choose English or Simplified Chinese in the installer. Desktop and Start menu shortcuts are independently selectable and initially checked. Upgrades retain previous choices and settings.
2. Open WordCatch and wait for it to be ready. Select text or point at a word, then press the lookup shortcut.
3. To change the app language, open **Settings & About → Interface language**. This changes interface text only: original text, dictionary meanings and cloud translations remain unchanged. Azure still translates into Simplified Chinese.
4. For cloud fallback, enter your own Azure Translator key and resource region, test the connection, enable cloud translation and save. No shared key is supplied, and cloud translation is off by default.
5. Closing the main window hides it to the notification area. Choose **Exit** to quit. You can change the lookup shortcut if it conflicts with another app.

Region OCR extracts English and ignores other-language characters. Multiline content is looked up separately; missing compounds receive split-word definitions. Cloud sentence translation requires an enabled Azure service.

Exit all WordCatch instances before installing or uninstalling. Installation uses versioned directories, replaces old product-name shortcuts and retains configuration compatibility. Reinstalling the same version is supported; downgrades are rejected. Uninstalling removes registered, unmodified application files and shortcuts, while keeping user configuration. Unrecognized files are not recursively removed.

## Dictionary, cloud and privacy

The bundled dictionary has 770,611 entries, including phrases, proper names and spelling variants; 768,739 have Chinese meanings. These are not 770,611 distinct basic English words. There are 365,948 letters-only entries, 400,183 when hyphens and apostrophes are allowed, and 58,313 inflection mappings.

Offline lookup provides dictionary definitions, not full-sentence machine translation. Azure accepts up to 2,000 characters per request. Only the query text is sent to Microsoft, never screenshots. Your own Azure subscription determines pricing and quota. The connection test sends only the fixed word `Hello`.

OCR and dictionary lookup run locally. Screenshots are processed in memory, and neither screenshots nor query history are saved. WordCatch reads the clipboard only during a shortcut lookup; it does not monitor it in the background. Only **Copy original** writes to the clipboard. Network errors and invalid keys do not prevent offline lookup.

Configuration is stored in `%LOCALAPPDATA%\TianqiStudio\WordLens`. API keys use Windows current-user DPAPI encryption; another Windows account cannot directly use the encrypted key file. Language, cloud and startup preferences are independent.

An optional personal phrase database can be placed at `%LOCALAPPDATA%\TianqiStudio\WordLens\anki-phrases.sqlite3`. Full input, selections and clipboard text match phrases first. Pointer OCR can match the longest phrase on the same line containing the pointer. Missing meanings are not invented. WordCatch does not extract Anki data during installation and works without this database.

## Updates and startup

In **Settings & About**, choose Automatic, tianqistudio.com or GitHub for update checks. Automatic checks are enabled by default, at most once every six hours; you can disable them or check manually. Automatic mode compares both sources and tolerates one unavailable source. It requests only public version information and never uploads lookup content.

Updates open a release page for manual download and installation. WordCatch does not download, execute or install updates automatically. The website hosts the [version manifest](https://tianqistudio.com/updates/wordcatch/latest.json); installers are hosted on GitHub Releases.

**Start with Windows** is off by default and applies only to the current Windows user. Save it to start in the notification area after login. Disable and save it to remove startup. Upgrades preserve the installing user's choice and update the executable path; uninstalling removes that user's startup entry. Other Windows users manage their own preferences.

## Development and tests

Built with C# / WPF, Windows UI Automation, RapidOCR with ONNX Runtime CPU, and an ECDICT SQLite dictionary. Shortcuts use DesktopShortcuts. See [third-party notices](docs/third-party.md) for sources and licenses.

A development setup requires Python 3.12 and the .NET 10 SDK. Run from the project root:

```powershell
.\Setup-WordLens.ps1
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
dotnet build src/WordLens/WordLens.csproj
.\Start-WordLens.ps1 -Development
```

Build a new installer with a unique build ID, then validate its payload without installing it:

```powershell
.\.venv\Scripts\python.exe tools/build_installer.py --build-id 0.7.0-005
.\.venv\Scripts\python.exe tools/verify_installer.py 0.7.0-005
$env:WORDLENS_QA_OUTPUT = "$PWD/artifacts/qa/bilingual-070"
& ./artifacts/installers/0.7.0-005/payload/WordLens.exe --self-test
```

The self-test uses synthetic text and test windows for OCR/selection checks, mock cloud responses, and a separate startup registry key. It does not verify every third-party app or a real Azure subscription. Installer archive validation checks embedded files without executing installation.

## Limitations and compatibility

Selection capture depends on the target app's UI Automation support; unsupported or timed-out selection falls back to OCR. Small fonts, complex backgrounds, animation and protected windows can prevent recognition. Automatic hover lookup, pronunciation and automatic update installation are not provided.

The editable icon is [wordcatch.svg](src/WordLens/Assets/wordcatch.svg), with PNG and ICO variants alongside it. Internal namespaces, executable names, installation IDs and configuration paths retain `WordLens` for compatibility.

## Copyright

Source is publicly available for viewing. Copyright and third-party license terms are in [NOTICE.md](NOTICE.md).

Made by [tianqistudio](https://tianqistudio.com/home).
