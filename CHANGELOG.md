# Changelog

What changed in each OpaGachi release, newest first.

## v1.0.0: first public release

### Added
- **Windows installer** (`OpaGachi Setup.exe`, inside `OpaGachi-1.0.0.zip`):
  - installs for the current Windows account, no admin rights;
  - a Start menu entry, an optional desktop shortcut, and an entry in Windows Settings > Apps.
- **Updates:** a newer Setup finds the install, closes OpaGachi, replaces the program files and keeps your
  settings. It won't silently downgrade a newer version.
- **Uninstaller:** removes OpaGachi's agent hooks first, then the program, and asks whether to delete your data.
- **Run at startup** (Settings > Startup): minimized to the tray, or with the Settings window open.
- **Restart prompt** after you add or remove hooks, plus **Restart** in the tray menu.
- **Hook reminder:** at start, names installed agents that have no hooks, with "Don't show this again".
- **Pets:**
  - one per open agent: Claude, ChatGPT, Gemini and Grok desktop apps, plus Claude Code, Codex, Gemini CLI and
    Grok Build;
  - states: thinking, working, image generation, needs you, done, failed, thanks, bored;
  - 24 fps animation and spring motion.
- **Settings window:**
  - Light, Dark and High contrast themes, text size, reduce motion;
  - placement and display choice, sparkles, tie designs, name colours, completion sounds;
  - a Usage card for all four agents: Claude and ChatGPT/Codex plan limits and tokens; Grok tokens and estimated
    cost from Grok's session records; Gemini answers per day (the Gemini app keeps no usage on the PC);
  - experimental custom sprites (five slots per action);
  - Factory reset; Help with FAQ, privacy and credits.

### Security
- The local hook server refuses requests from web pages (browser `Origin` header or a foreign `Host`).

### Notes
- Settings and anything you add live in `%LOCALAPPDATA%\OpaGachi`; updates never touch them.
- Not code-signed yet: SmartScreen warns, and Smart App Control blocks the installer.
