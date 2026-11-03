# OpaGachi 1.0.0

Formerly OPA PET (Gen 1 to Gen 2.3). The app is OpaGachi; Opa, the character, is a separate Slushie4LiFe.com character.

Created by Slushie4LiFe; special thanks to Quil (Quest) and BigRedSoda (Jdyn). Opa is a Slushie4LiFe.com
character and falls under natural copyright laws from its creator. More at Slushie4LiFe.com.

Desk pets that show what your AI agents are doing. Each open agent session gets a small animated
character above the taskbar that thinks, works, raises its hand when it needs approval, sulks when
something fails, celebrates when it's done and thanks you when you look.

Pets exist for **Claude, ChatGPT, Gemini and Grok**, each with its own tie and name colour. OpaGachi finds
them by itself (desktop apps and command-line agents only; websites never get pets):

- **Claude Code** sessions, through each session's status file and transcript (nothing to install), or
  exactly through optional hooks.
- The **ChatGPT desktop app and Codex**, through Codex's local thread logs (nothing to install), plus an
  optional hook for approval prompts.
- **Desktop apps** (ChatGPT, ChatGPT Classic, Claude, Gemini and Grok, `grok-app.exe`): opening an app
  brings its pet in, closing it sends the pet away, and its prompt box shows when it's answering.
- **Gemini CLI** and **Grok Build** (xAI's terminal agent) through optional one-click hooks. Grok Build
  also runs Claude Code's hooks, so with those installed it works with nothing extra.

A generic HTTP endpoint is there for testing and future adapters.

## Quick start

```
build.cmd                 # compiles bin\OpaGachi.exe (uses the C# compiler built into Windows)
bin\OpaGachi.exe --demo     # starts the pets and plays a walkthrough of every animation
package.cmd               # the release: dist\OpaGachi\, dist\OpaGachi Setup.exe and the download dist\OpaGachi-<version>.zip (--skip-build while the app runs)
```

On first run a **Welcome** window offers to connect Claude Code and play the demo.
Double-click the tray icon (the red ring) for **Settings**, or right-click it for:

- **Settings...**: a small borderless "liquid glass" window (no title, can't be resized or maximised;
  drag it by any bare spot) with six tabs along the top and the live pet summary in the footer. Every
  change applies and saves straight away. Pets, Settings and Help work like the iPhone Settings app: one
  grouped card of categories (tinted icon, current value, chevron); a category slides in its own page with
  a back capsule (clicking the tab again also goes back). Changing the theme, contrast or text size rebuilds
  the window in place, on the same tab and category.
  - **Pets:** categories Pet size, Placement (position, display, spacing, where pets may show), Behaviour (bored
    timer, fidget chance, badges) and Interaction.
  - **Agents:** a carousel showing each agent's own pet in its idle loop and tie, then rows to switch the
    pet on or off and pick its name colour (gradient presets: Claude orange, ChatGPT astral purple, Gemini
    pearl, Grok black to silver, each agent's default; or white, or a colour of your own), tie pattern and tie colour.
  - **Connections:** one row per source (status dot, method, one action: add or remove a hook), including
    the Claude plan limits relay and the Codex plan.
  - **Usage:** one card per agent: the 5 hr and 168 hr plan limits as rings and, under a hairline, today's
    tokens, the 30-day sum and a heat map of the 30 days (3 rows of 10, today bottom right).
  - **Settings:** categories Appearance (Light / Dark), Accessibility (reduce motion, text size, high
    contrast), Sound (completion sound on/off; a sound from `assets/sounds` in alphabetical order, or your
    own MP3/WAV), Experimental (custom sprites; custom pet location), Privacy and legal, Credits.
  - **Display** (Pets › Placement): the main display or any connected one, listed with its size and
    orientation (`Display 1 · 1080 × 1920 · portrait`). Pets use that display's work area, so portrait
    screens and side taskbars place correctly; a display that's unplugged falls back to the main one.
  - **Custom pet location** (experimental): shows the bare desktop of the pets' display (Windows renders
    the desktop window, so wallpaper and icons appear without hiding any app) with the pets on a dotted
    line; drag it (arrows nudge, Shift for bigger steps), Save. The row is then centred on that spot with
    its feet on the line, stored as fractions of the work area (`customX`, `customY`).
  - The window ends with a small legal line: © 2026 Slushie4LiFe.com.
  - **Help:** categories Questions, Privacy and legal, Credits and Troubleshooting (the diagnostic log).
- **One row per agent** with a live status dot ("Claude — Working (CLI)"): show its window, turn its
  pets on or off, jump to its settings.
- **Position**: six anchors (top or bottom × left, center or right).
- **Demo**: rerun the walkthrough, or play any animation on demand.
- **Open log** and **Exit**.

Nothing to install: the app runs on .NET Framework 4.8, which ships with Windows 10 and 11.

## How agents are detected

Each source runs on its own low-priority background thread and only reports changes; the pets react on
the UI thread. Measured cost is in the `perf: sources` line of `opagachi.log`.

| Source | Arrives / leaves | Think | Work | Help | Complete | Fail |
|---|---|---|---|---|---|---|
| Claude Code, no hooks (`~/.claude/sessions/<pid>.json` + transcript) | session found / process exits | prompt, thinking, reading tools | Bash, Edit, Write… | AskUserQuestion, ExitPlanMode | status back to idle / end of turn | tool error, API error |
| Claude Code hooks (optional, exact) | SessionStart / SessionEnd | see table below | | permission prompts too | Stop | StopFailure |
| ChatGPT app + Codex (`~/.codex/sessions/**/rollout-*.jsonl`) | app window, or thread activity | task started, reasoning, web search | shell, patches, other tools | optional Codex hook | task complete (last busy thread) | error event |
| Desktop apps via UI Automation (`assets/detect.json`) | main window opens / closes | stop button appears | still answering after 8 s | approval buttons (Claude, ChatGPT; Grok's are guesses) | stop button gone | an error button after an answer ("Try again" for Claude, which shows "Retry" under every reply) |

Notes:
- The Codex logs don't record approval requests; the optional Codex hook covers them. It prints nothing,
  so Codex still shows its own approval prompt, and Codex runs it only after you trust it with `/hooks`.
- When Claude Code hooks are installed they win; the transcript reader is ignored for those sessions.
- UI Automation is the accessibility interface screen readers use. Each second, only the buttons around
  the app's prompt box are read (well under 1 ms). The whole window is searched only to find the prompt
  box, and once after each answer to look for an error. While it's in use, Chromium-based apps keep
  their accessibility tree up to date, which costs them a little CPU.
- `assets/detect.json` lists the apps (exe names, stop-button pattern, approval and error button names).
  It's editable without rebuilding. Every change in an app's prompt-box buttons is logged
  (`prompt box buttons: …`), which is how the patterns are tuned.
- A pet whose app window closes leaves once it has nothing left to show, so a finished answer still
  gets its Complete moment.

### Hidden windows

Chromium-based apps (all of the desktop AI apps) **freeze their accessibility tree while minimised or
fully covered**. The page keeps running, but nothing outside can see it change until the window shows
again, and then it catches up at once (tested in an isolated Edge window). CPU and I/O were tested as a
stand-in and measured the same as idle while a covered app streamed a reply, so they aren't used.
A reply that finishes while its app is hidden is picked up when:
1. the window shows again (the pet catches up straight away), or
2. the app **flashes its taskbar button**, which desktop apps do when a reply lands in the background.
   Windows reports flashes through the shell hook at no cost. Every flash from a watched app is logged.

## Usage tab

| Agent | What's shown | From |
|---|---|---|
| Claude | Tokens per day for 30 days (today, the 30-day sum, a heat map) | the Claude Code transcripts (`UsageHistory`) |
| Claude | 5-hour and weekly plan limits with reset times | the optional **status line relay** (Claude Code only gives these to status line scripts) |
| ChatGPT / Codex | 5-hour and weekly plan limits; tokens per day for 30 days | `token_count` events in the Codex logs (the plan name shows on the Connections page) |
| Grok | Tokens per day for 30 days and the estimated cost; a note that plan limits stay in Grok's `/usage` | each session's `~/.grok/sessions/**/usage.json` (the documented `grok usage` envelope: per-turn `endedAt`, `totalTokens`, `costUsdTicks` at 10^10 per USD). Re-read when a file changes; a turn repeated by a forked session counts once. Grok gives scripts no rate-limit summary. |
| Gemini | Answers per day for 30 days, counted by OpaGachi when the Gemini pet finishes a reply | `AnswerLog` (`answers.json` in the data folder; every agent's answers are recorded, only Gemini's are shown). The Gemini app is a web page in a window and keeps no usage on the PC. |

The relay (`--install-statusline` / `--uninstall-statusline`, or Settings → Connections → Claude plan limits):
- It replaces the status line command with `OpaGachi.exe --statusline <your command, base64>`.
- That forwards the JSON to the app, then runs your command with the same input and passes its output
  through byte for byte.
- Removing it restores your command exactly. A backup of `settings.json` is kept.

## Pets you can click

- **Click** a pet to bring its app or terminal to the front. Only the figure catches clicks (a layered
  window lets clicks through wherever it's transparent), and pets never take focus.
- **Hover** to see "Name · Working" in the name tag. Text too wide for the pet shrinks to fit. Clicking
  the name tag does the same as clicking the pet (it no longer opens a usage card).
- **Right-click** for: show window, turn this agent's pets off, Settings.
- Both can be switched off in Settings → Pets → Interaction, which makes pets click-through again.

## Connecting Claude Code hooks (optional)

After any hook is added or removed on **Connections** (or added from Welcome), OpaGachi asks
"For pets to work properly, OpaGachi needs to restart. Restart now?" with **Restart** or **Later**. Restart
starts a new copy with `--restart-after <pid>` (it waits for the old one to exit) and, from Connections,
reopens Settings on that page. On start, if an installed agent (Claude Code, Codex, Gemini CLI, Grok Build)
has no OpaGachi hooks, a reminder names them and offers **Open Connections**, **Later** and **Don't show this
again** (`hookReminder: false`). It stays quiet on `--background` starts and when Welcome is showing.
The tray menu's **Restart** does the same restart at any time (for "Later").

Use the tray menu (**Claude Code → Install hooks**), or run:

```
bin\OpaGachi.exe --install-claude-hooks                        # all sessions (~/.claude/settings.json)
bin\OpaGachi.exe --install-claude-hooks D:\proj\.claude\settings.local.json   # one project only
bin\OpaGachi.exe --uninstall-claude-hooks [path]               # remove them again
```

Install keeps your other settings and hooks, writes `settings.json.opagachi-backup` first, and is
safe to run more than once. It adds:

- **HTTP hooks** on 12 events, posting to `http://127.0.0.1:47321/claude-code` (2 s timeout).
  They arrive in order, and if OpaGachi isn't running Claude Code just carries on.
- **One async command hook** (`OpaGachi.exe --hook-hostinfo`) on SessionStart and UserPromptSubmit.
  It finds the session's terminal window, so the pet knows when you've looked at finished work,
  and the `claude.exe` process, so the pet leaves when the session closes.

| Claude Code event | Pet |
|---|---|
| UserPromptSubmit | Think (Thank first if it was waiting on you) |
| PreToolUse: Read, Grep, Glob, WebSearch, WebFetch, Agent… | Think ("research counts as thinking") |
| PreToolUse: Bash, Edit, Write and anything else | Work |
| PreToolUse: tools with "image" in the name | Image Generation |
| PermissionRequest, Notification(permission_prompt), AskUserQuestion, Elicitation | Help |
| Next tool event after Help | Thank, then carries on |
| PostToolUseFailure, PermissionDenied | Fail (plays once, to the end), then carries on |
| StopFailure | Fail, holds until you send a new prompt, then Thank |
| Stop | Complete (loops until you focus the terminal), then Thank, then Idle |
| No activity | Idle, Idle Occasional at random, Bored after 10 min |
| SessionEnd, or the claude process exits | pet fades out |

## Other adapters / testing

```
curl -X POST http://127.0.0.1:47321/signal -d "{\"agent\":\"gemini\",\"instance\":\"tab1\",\"signal\":\"Work\"}"
curl -X POST http://127.0.0.1:47321/signal -d "{\"agent\":\"gemini\",\"instance\":\"tab1\",\"anim\":\"Thank\"}"
curl -X POST http://127.0.0.1:47321/signal -d "{\"agent\":\"gemini\",\"instance\":\"tab1\",\"close\":true}"
curl http://127.0.0.1:47321/state
```

Signals: `Opened, Think, Work, ImageGen, NeedsApproval, Approved, Denied, Error, FatalError,
UserPrompted, Done, Viewed, Cancelled`. Agents and tie colours live in `assets/agents.json`.

Other optional hooks (all also in Settings → Connections; each keeps a backup and removes only its own entries):
- **Codex:** `--install-codex-hook` / `--uninstall-codex-hook` (`~/.codex/hooks.json`).
- **Gemini CLI:** `--install-gemini-hook [path]` / `--uninstall-gemini-hook [path]` (`~/.gemini/settings.json`).
- **Grok Build:** `--install-grok-hook` / `--uninstall-grok-hook` (its own file, `~/.grok/hooks/opa-pet.json`).

The listener binds to 127.0.0.1 only. Any local program (or a web page posting to localhost) could
fake a pet's state. That's harmless for now, but worth a token before shipping.

## Sprites

Source clips live in `..\1TEMPLATES` (VP9 .webm with transparency, 384×384 at 24 fps; one folder
per animation, with Idle split into LOOP and OCCASION). Convert them with:

```
build-assets.cmd --height 224 --fps 24 --crop 26,8,341,370 --icon ""   # how the shipped clips are built
```

The pipeline crops every clip to the same box (so feet line up), scales it (224 px tall, 24 fps since
2026-11-02: every source frame, for smoother motion; about 87 MB), finds the rest frames, and
writes `assets/sprites/_template/<animation>/*.opaclip`. Each file holds deflate-compressed XOR
deltas of premultiplied BGRA frames, plus a per-frame mask of the blue tie. The app recolours the
tie per agent at runtime, keeping its shading. It also writes `preview.png` (a contact sheet) and
`assets/icon.ico`.

**Adding animations** (2026-11-02): every animation is one line in `AnimSpec.All` (`src/SpriteLibrary.cs`):
its folder, the mode it loops for, or "plays once".
- A new variant for any state needs no code. Put the `.webm` in `..\1TEMPLATES\<FOLDER>` and run the build
  above; every clip in a state's folder is picked at random.
- A brand-new animation is one `Anim` value and one `AnimSpec` line (plus a `Mode` if it's a new state).
- Clips can have any frame rate; rest frames are optional (without them a loop hands over at its end).

**Tie patterns:** an agent can have `"texture": "assets/ties/<file>.png"` in `agents.json` (set from
Settings → PNG... / Paste). The pattern is shrunk to an 18 px tile and shaded by the tie mask.
There are presets in `assets/ties/presets/` (stripes, plaid, houndstooth, dots, stars, argyle).

**Status badges:** vector badges (`src/Badges.cs`) float next to the head for Help, Complete, Fail,
Think and Bored (`"badges": false` in settings.json turns them off). The old PNG badges are kept in
`design/badges-legacy/`. Pets also do a small hop when they start needing attention.

**Custom sprites (experimental):** Settings → Experimental → Custom sprites replaces every agent's
sprite with one generic sprite (no tie; name colours still apply). Choose a `.png` (a still) or a
`.webm` with transparency (up to 10 s). A `.webm` is decoded by Microsoft Edge running hidden on a
temporary local page (each frame goes through a canvas into Edge's log), so nothing extra is
installed. Either way the result is an ordinary clip at `assets/sprites/custom/idle/custom.opaclip`.
Self-check: `bin\OpaGachi.exe --import-sprite file.webm | more` prints the frame count.

UI design (the Settings and Welcome windows, badges, tie patterns) was first drafted with Gemini.

**Sizes and standing poses:** clips are built at 224 px and scaled live (bilinear, supersampled
when shrinking), so every size stays crisp. "Normal" is 160 px tall. Clips listed in
`assets/sprites/_template/poses.json` are full-body standing poses. They look smaller than sitting
ones at the same scale, so they get a fixed **standing pose boost** of 15% (`Settings.StandingBoost`).
Pets stay anchored at the feet.

**Default ties:**

| Agent | Tie |
|---|---|
| Claude | Claude orange `#D97757` |
| ChatGPT | astral galaxy purple (pattern) |
| Gemini | pearlescent pink-white (pattern) |
| Grok | black with charcoal stripes (pattern) |

`tools/make_ties.py` generates the three patterns as seamless tiles. The galaxy reuses the stars of
Gemini's provided preset, and the stripes follow her stripes preset.

**Per-agent sprites (later):** put drawn clips in `assets/sprites/<agent>/<animation>/` (e.g. run
the pipeline with `--src ..\1CLAUDE --out assets\sprites\claude --crop 26,8,341,370 --icon ""`).
Any animation an agent has its own clips for is used as drawn. Everything else falls back to the
template with a recoloured tie.

## Where things live

| What | Where | Replaced by updates? |
|---|---|---|
| Program: `OpaGachi.exe`, `uninstall.exe`, `assets\` (sprites, stock sounds, tie presets, fonts, `detect.json`, `factory-agents.json`) | install folder, default `%LOCALAPPDATA%\Programs\OpaGachi` (dev: this folder, exe in `bin\`) | yes, wholly |
| User data: `settings.json`, `agents.json`, `hook-paths.json`, `opagachi.log`, `sprites\custom\`, uploaded `ties\` and `sounds\` | `%LOCALAPPDATA%\OpaGachi` (`Program.Data`) | never |

- The app only reads its install folder. Every write goes to the data folder, so a sprite pack, preset or patch can replace `assets\` without touching anything the user made.
- Until the user changes an agent, `assets\factory-agents.json` is used; the first change saves `agents.json` in the data folder. Uploaded ties are stored there and referenced by absolute path; presets stay relative to the install folder.
- Stock sounds ship in `assets\sounds\`; added ones go to the data folder's `sounds\`, and both are listed together.
- Factory reset empties the data folder (keeping the log) and removes OpaGachi's hooks; the shipped defaults then apply.
- Before 1.0 everything lived in the app folder. On the first start of 1.0, `settings.json`, `hook-paths.json`, `assets\agents.json` and `assets\sprites\custom\` are copied to the data folder once; the originals stay as a backup.

## Releasing an update

1. Bump the version in `src\AssemblyInfo.cs` (semantic versioning: patch for fixes, minor for features, major for breaking changes). `src\app.manifest`'s identity version can follow.
2. Add the release notes to `CHANGELOG.md`.
3. Run `package.cmd`. It builds `bin\OpaGachi.exe`, assembles `dist\OpaGachi\` (exactly what gets installed, plus `uninstall.exe`), zips it and embeds it in `dist\OpaGachi Setup.exe`. Nothing else is needed: the installer (`installer\Setup.cs`) is compiled by the same built-in C# compiler.
4. Run `tools\dev\review_capture.ps1`, then after packaging `tools\dev\wizard_test.ps1` (the setup wizard driven through its buttons offscreen) and `tools\dev\setup_test.ps1` (silent install, update, downgrade, refusal and uninstall against fixture hooks under `OPAGACHI_HOME`; it briefly creates and removes the Start menu and Apps entries).
5. Ship `dist\OpaGachi-<version>.zip` (the installer plus "How to install.txt"; built by `installer\zip.ps1`), or `OpaGachi Setup.exe` on its own. Running it over an existing install closes OpaGachi, replaces the program files (old `assets\` is cleared first, so removed files don't linger), keeps the data folder, shortcuts and hooks, and updates the Apps entry.

Installer switches: `/silent`, `/dir=PATH`, `/desktop`, `/launch` (install or update without the wizard). Setup never downgrades unattended: if a newer version is installed, `/silent` exits with code 2 unless `/force` is given; the wizard says "Replace ... with older" and labels its button Downgrade. `uninstall.exe [/silent] [/purge]` removes OpaGachi's hooks (via `OpaGachi.exe --remove-hooks`), the program folder, shortcuts and the Apps entry; `/purge`, or Yes when asked, also deletes the data folder. Per-user only: no admin rights, nothing in Program Files or HKLM.

## Code signing (needed before release)

Windows 11's **Smart App Control** blocks every app that isn't signed by a certificate authority Microsoft trusts,
with no "Run anyway" (confirmed on a test PC, 2026-11-02). It checks `OpaGachi Setup.exe`, and also
`OpaGachi.exe` and `uninstall.exe` when they run. SmartScreen warns about unsigned downloads too. Only a signature
fixes this; there is no setting inside the app that can.

1. **Get a code-signing certificate in the publisher's name** (identity check and payment, so only the owner can):
   - an OV or EV certificate from a certificate authority (for example Sectigo, DigiCert, SSL.com, Certum). The key
     comes on a USB token or a cloud HSM; once its software is installed, the certificate shows in Windows under
     Current User > Personal, which is all `installer\sign.ps1` needs;
   - or Microsoft's **Azure Trusted Signing** (monthly subscription; check it's offered in your country). It signs
     through its own client (signtool with its plug-in, from the Windows SDK) instead of `sign.ps1`, so sign the
     same files at the same two points in `package.cmd`.
2. **Build signed:** `set OPAGACHI_CERT=<certificate thumbprint>` and run `package.cmd`. It signs `OpaGachi.exe` and
   `uninstall.exe` before they go into the payload, then `OpaGachi Setup.exe`, with SHA-256 and a DigiCert timestamp
   (signatures stay valid after the certificate expires). It warns if the certificate isn't trusted. Without
   `OPAGACHI_CERT` it builds unsigned and says so. Check with PowerShell
   `Get-AuthenticodeSignature "dist\OpaGachi Setup.exe"` (Status should be `Valid`).
3. **Testing before you have a certificate:** turn Smart App Control off on the test machine (Windows Security >
   App & browser control > Smart App Control settings > Off). On many Windows 11 versions it can't be turned back
   on without resetting Windows, so only do this in a throwaway sandbox or VM. Microsoft's file review (the
   "Microsoft will review the app" message) covers one exact file, so every rebuild would need a new review.

The signing path was tested with a temporary self-signed certificate: all three files signed and timestamped, and
the signed Setup passed `setup_test.ps1`. A self-signed certificate isn't trusted, so it doesn't get past Smart
App Control.

## Settings (`settings.json` in the data folder, created when you change something)

| Key | Default | |
|---|---|---|
| anchor | BottomRight | BottomLeft, BottomCenter, BottomRight, TopLeft, TopCenter, TopRight |
| port | 47321 | must match the installed hooks (reinstall after changing it) |
| boredAfterSeconds | 600 | idle time before Bored |
| occasionalEverySeconds / occasionalChance | 8 / 0.2 | Idle Occasional rolls |
| spacing | 0.7 | distance between pets, as a fraction of the sprite width (Settings offers 0.4–1.2 in steps of 0.1) |
| margin | 16 | distance from the screen edge (px) |
| size | 2 | 0 XS, 1 S, 2 Normal, 3 L, 4 XL (0.6×, 0.8×, 1×, 1.2×, 1.4×) |
| visibility | AllButOwnApp | see below |
| badges / showWelcome | true / true | status bubbles; first-run window |
| hookReminder | true | missing-hooks reminder at start; "Don't show this again" sets it to false |
| appearance / highContrast / textSize | light / false / 1 | theme; solid high-contrast surfaces; 0 Small, 1 Default, 2 Large, 3 Larger |
| sound / soundFile | false / Comet.mp3 | completion sound and which file in `assets/sounds` |
| customSprites | false | experimental: one generic sprite for every agent |
| display | "" | the pets' display (`\\.\DISPLAY1`…); empty = the main display |
| customPlacement / customX / customY | false / 0.5 / 1 | experimental custom location: the row's centre and ground line, as fractions of the work area |

**Where pets show (`visibility`):**
- `DesktopOnly`: pets sit just above the desktop, so any app window covers them.
- `AllButAiApps`: on top of everything, but every pet hides while an AI app is in front. That
  means any agent's app from `agents.json`, or the terminal of a Claude Code session.
- `AllButOwnApp`: on top of everything, and each pet hides while its own AI is in front.

`agents.json` per agent: `name`, `tie`, `texture`, `nameColor` (`#RRGGBB`, or a preset: `orange`, `astral`,
`pearl`, `silver`; left out = the agent's own preset), `enabled` and `apps` (process names, e.g. `ChatGPT.exe`).

## How it works

```
Claude Code ──HTTP hooks──►┐
--hook-hostinfo / --codex-hook ►├─ EventServer (127.0.0.1) ─► queue ─┐
curl / future adapters ───►┘                                        ├─► PetManager (UI thread, 25 Hz tick)
ClaudeSessions (thread) ───►┐                                       │    ├─ Pet: state machine + ClipPlayer
CodexWatcher   (thread) ───►├─ Sources.Queue ──────────────────────┘    └─ PetWindow: layered window
AppWatcher     (thread) ───►┘
```

**Playback rule:** a clip is never cut off, with one exception.
- **Help cuts in at once** (user, 2026-11-01): whatever is playing stops and the Help clip starts.
- One-shots (Thank, Fail, idle fidgets) always play to their last frame.
- Looping clips (Think, Work, Idle…) hand over only at their last frame or at a **rest frame**,
  where the character is back in its starting pose (`rest.json`, from `tools/find_rest_frames.py`).
- The badge and attention hop show a new state straight away; the body follows at the next clean point.
  With 14–16 s clips, that can take a few seconds (the Work clip has no rest frame).
- **Complete always shows:** looking at the agent's window (or clicking the pet) turns Complete into
  Thank only after the Complete clip has been on screen for 3 s.

**Motion** (Gen 1.6, from Gemini's motion spec; `src/Choreography.cs`):
- The whole window moves for arriving, leaving, hiding and gliding along the row; the figure moves
  inside it for hops, squash and stretch, shivers, the hover lift and the click pop, so the contact
  shadow and the Plaza-style ground rings under the shoes stay put.
- Arrival (polished 2026-11-01): the pet rises 30 px (24 px when joining a row) on the Bouncy spring,
  fading in over 220 ms; it stretches with its speed on the way up, squashes as it overshoots and
  settles, and a tie-tinted ring spreads as it lands. Joiners start after their neighbours make room.
- Departure: the outermost pet lifts 4 px, then sinks and shrinks (360 ms); one from the middle sinks
  over an imploding ring (320 ms) and the others start closing the gap at once: one after another
  (50 ms apart, capped at 240 ms) on the Smooth spring, never overshooting, each with a 3 px step-hop.
  A pet already gliding just retargets.
- Attention fires at once: Needs you = two hops and a ground beacon every 2.4 s (with a hand glow and
  sound arcs from the ! badge); Complete = one hop with green and tie rings plus the agent's signature
  effect; Fail = a sideways shiver.
- Per-character effects (`src/Effects.cs`) on Complete and Thank, smaller ones on arrival, hover and
  idle; the window ticks at 30 fps only while particles live.
- Hidden pets (their own app in front) fade out over 260 ms, keep their slot, and stop catching clicks.
- Reduced motion (Settings → Settings → Accessibility, or Windows' "Animation effects" off) keeps only short fades.

- `src/Pet.cs`: modes (Idle, Bored, Think, Work, ImageGen, Help, Complete, FailedHold) plus one-shots
  (Thank, Fail, Idle Occasional), the playback rule above, a queue so Thank then Fail both show,
  3-frame cross-fades between clips, and eased motion.
- `src/SpriteClip.cs`: lazy clip loading, forward delta decoding, tie recolouring.
- `src/PetWindow.cs`: per-pixel-alpha, click-through, topmost window: ground decor, sprite, effects
  and badge in one DIB; the hover name pill is re-rendered only while it grows.
- `src/Choreography.cs` (pose per tick), `src/Effects.cs` (particles), `src/Badges.cs` (vector badges).
- `src/UsagePage.cs`: Settings → Usage (ring gauges and the 30-day heat map).
- `src/Sound.cs`: completion sounds through Windows' MCI (MP3 and WAV, nothing to install).
- `src/CustomSprite.cs`: the custom sprite import (PNG directly, WebM through a hidden Edge).
- `src/Legal.cs`: the privacy policy, terms, data notice and credits shown in Settings and Help.
- `src/Icons.cs`: SVG path parser and the line icons; `src/Tip.cs`: tooltips.
- `src/ClaudeCode.cs`: event mapping, host-window discovery, hook install and uninstall.
- `src/ClaudeSessions.cs`, `src/CodexWatcher.cs`, `src/AppWatcher.cs`, `src/CodexHooks.cs`: the activity
  sources (see "How agents are detected"); `src/Sources.cs`: their queue, file tailing and process helpers.
- `src/EventServer.cs`: tiny HTTP server that answers before parsing, so hooks never wait.
- `src/Theme.cs`: design tokens and helpers for the liquid glass look: Light, Dark and High contrast
  palettes (translucent tokens carry alpha), the backdrop with its colour pools, the glass panel painter,
  Inter (loaded privately from `assets/fonts`, scaled by the text size setting), section headers and
  glass cards, themed menus. New UI should use these tokens.
- `src/Scaler.cs`: live resampling of each frame to the pet size (skips empty space around the figure).
- `src/Controls.cs`: owner-drawn see-through controls (capsule buttons, slider, toggle, stepper, status
  dots, the Agents preview), the top `TabBar`, `GlassForm` (borderless window: backdrop, rounded corners,
  shadow, drag, caption buttons), `GlassScroller` (paints its share of the backdrop in page coordinates, so a
  scroll step is a plain blit), and `DrillPage`, `CategoryRow`, `BackHeader` (the category lists and pages).
  `GlassCard` (in `Theme.cs`) renders its backdrop + glass once into a bitmap that every row reuses.
- `src/SettingsForm.cs`: the Settings window (tabs + pages of glass cards, live apply).
- `src/Spring.cs`: the motion system from Gemini Response 5. `Spring` is a damped spring (response +
  damping fraction, 4 sub-steps, retargets keep their velocity, Reduce Motion snaps); `Feel` holds the
  five tokens (Snappy, Bouncy, Smooth, Gentle, Settle) and a closed-form curve for one-shot motion.
  Colour and opacity tweens use the duration tokens in `Theme.cs` (Press 50, Fast 100, Standard 150,
  Crossfade 250 ms). Self-check: `tools\dev\spring_check.cs` (build line at its top).
- `src/Controls.cs`: `Animator` (one ~60 fps timer that runs only while something moves) and the
  animated owner-drawn controls. `src/Integrations.cs` is the shared status model for Settings, Welcome
  and the tray menu. `src/Usage.cs` reads token usage. `src/AgentHooks.cs` holds the Gemini CLI and Grok
  Build hooks and the status line relay.
- Dev flags: `--demo` (walkthrough), `--settings [--page 0..5]` (open Settings on a page),
  `--menu` (show the tray menu), `--welcome` (show the Welcome window), `--import-sprite <file>`,
  `--placement` (open the custom location editor on its own; the result goes to the log).
  Environment `OPAGACHI_HOME=<folder>` replaces the user profile for every agent folder (`.claude`, `.codex`,
  `.gemini`, `.grok`) and the data folder (`<folder>\AppData\Local\OpaGachi`), so tests, Factory Reset and
  the uninstaller only see fixtures there. `--remove-hooks` removes every OpaGachi hook (used by the uninstaller).
- The event server refuses anything a web page could send: a request with a browser `Origin` header, or with a
  `Host` other than `127.0.0.1`/`localhost` (DNS rebinding). Only local programs (agent hooks, our helper) get in.
- Dev endpoints for checking motion without touching the mouse: `POST /settings {"page":N}` (open
  Settings on a page), `POST /settings {"dark":true}` (switch an open Settings window's theme the way its
  dropdown does); `POST /signal` with `{"hover":true}`, `{"click":true}` and `{"snapshot":"x.png"}` (a pet
  window's own pixels).
- `tools/make_icon.py` rebuilds `assets/icon.ico` (exe, windows, taskbar) from
  `design/newicon-source.jpg`. The tray icon is drawn in code at the tray's size (the red ring on a
  clear background, plus the status dot), because the scaled-down app icon was a dark blur there.
- Scriptable settings: `curl -X POST http://127.0.0.1:47321/settings -d "{\"size\":4,\"visibility\":\"DesktopOnly\",\"enable\":{\"grok\":false}}"`.
  `GET /state` also reports the window in front, which helps when checking the visibility rules.

Footprint measured on the dev machine (from the `perf:` lines in `opagachi.log`, logged once a minute):

| Build | Pets animating | Frame loop CPU | Working set |
|---|---|---|---|
| Gen 1 (160 px clips, no scaling) | 4 | 1.0–1.4% of one core | 81 MB |
| Gen 1.3 (224 px clips, live scaling), Normal size | 5 | about 4.9–5.1% of one core | 91–94 MB |
| Gen 1.6 (choreography, effects, usage hub), S size | 1 at rest | 1.36% of one core | 99–128 MB |
| Gen 2.2 (24 fps clips, 16 ms frame loop), Normal size | 2 | about 4.5% of one core (was 2.5% at 12 fps) | 123 MB |
| Gen 1.6 stress test (5 pets cycling Complete/Thank every second) | 6 | 7.4% of one core; effects ≈ 0.05 ms per frame | 109 MB |

The Gen 1.3 cost per second for 5 pets breaks down as:
- decode: about 25 ms;
- tint and scale: about 15 ms;
- putting frames on screen: about 10 ms.

Clips change on 84% of rows every frame (video-style animation), so row-skipping wouldn't help much.
Options:
- a 160 px clip set for XS, S and Normal;
- cropping each frame's delta to the figure's columns;
- one continuous deflate stream per clip.

The large clip arrays are compacted at most every 30 s to keep the working set from creeping.

## Known gaps (next steps)

- The Grok app's approval and error button names are guesses until it answers once (see `prompt box
  buttons` in the log). Gemini's and Claude's were confirmed from real answers on 2026-11-01.
- Grok Build and Gemini CLI hooks are built to their published docs and tested with simulated events.
  Neither keeps usage records OpaGachi could check yet (no Grok Build sessions on the dev machine; Gemini
  CLI isn't installed), so the Usage page has no section for them.
- The installer is per-user. Releases must be code-signed (see "Code signing"): unsigned builds are blocked by Smart App Control.
- With clicking on, a pet's body covers whatever is under it (for example desktop icons near the taskbar).
- The product is desktop only; there's no browser integration.
- Primary monitor only, and sprites aren't scaled for high-DPI screens.
- With several Windows Terminal tabs, "viewed" means the terminal window is focused, not the exact tab.
- The ground decor (shadow, rings, hover glow) has visible pixels, so with clicking on it also catches
  clicks in a small strip under each pet's shoes.
- The tray icon and the custom sprite import were checked from rendered output and a test video, not
  yet with a real mouse or a real sprite.

## Gen 2.3: custom action slots, sparkles, new ties and detection

- Settings > Experimental > Custom sprites reveals one card per action. Each card opens its slots; Manage all animations shows all actions and Clear all custom sprites.
- Exact file stems: `WALK_1`, `IDLE_1`, `IDLE_OCCASIONAL_1`, `BORED_1`, `THINK_1`, `WORK_1`, `IMAGEGEN_1`, `HELP_1`, `FAIL_1`, `COMPLETE_1`, `THANK_1`. Suffixes 1 through 5 only, uppercase. Same stem replaces that slot. PNG or alpha WebM; the first successful import locks ALL actions to that format until Clear all. WebMs: max 40 MB / 10 seconds, decoded at 24 fps. Non-alpha videos are rejected using the source frame, before padding. Imported clips live in `assets/sprites/custom/<action>/ACTION_N.opaclip`; global lock is `custom/format.txt`. Old generic custom clips must be cleared first.
- Variants rotate without immediately repeating where possible. Missing action falls back to custom Idle, then built-in clips without tie tint. Walk is used during idle row movement.
- Agents has per-agent `sparkleTrigger` (On Hover / On Click / Both) and `sparkleColor` (24 named choices or null to follow the name colour). All signatures use Gemini's prism/glint design. Completion/arrival effects still play independently of the hover/click preference; Reduce motion suppresses all effects.
- The tie picker has six groups preserving the source design folders, with three designs each. Runtime presets use `assets/ties/presets/<group>/design-N-60x150.png`.
- Right-click a pet > Close Window > Confirm/Cancel dismisses that pet, leaving its agent running. Automatic signals cannot respawn it until its presence ends and returns, or OpaGachi restarts.
- Settings > Factory reset removes preferences, custom sprites, uploaded ties/sounds and OpaGachi's known hooks, preserving unrelated hooks/configuration. Defaults are `assets/factory-agents.json` and the stock `assets/sounds/` (1.0: reset empties the data folder; see "Where things live"). Custom hook install paths are tracked in `hook-paths.json` from this version onward. Historical custom paths outside the app's ancestor projects are not discoverable without a prior record. Reset uses a delayed `--after-reset PID` restart to avoid the single-instance mutex race. Never test the real reset unattended; `FactoryReset.ResetFiles` is tested in scratch instead.
- Grok's lowercase/snake_case hook names now map correctly; its completion returns to Idle after at least three seconds of Complete, through Thank. Desktop polling uses enabled/on-screen Stop/Cancel button names, never CSS substring guesses.
- Claude hook and host arrivals must match a live interactive session file; stale/misidentified hook sessions cannot create extra Claude pets. Multiple sources share the session key.
- Codex reconstructs in-progress state when following a recent rollout at startup, handles task_complete/task_completed, ignores guardian review logs, and takes precedence over desktop button polling while a desktop rollout is tracked.
- `--background` starts without showing Welcome and leaves saved preferences intact.
- Settings > Startup > **Run at startup** writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\OpaGachi` = `"<exe>" --background` (Start as: Minimized to tray) or `"<exe>" --settings` (Settings window). The uninstaller and Factory reset remove it.

Regression/render harness: `tools/dev/review_capture.ps1` (49 checks, including a full `FactoryReset.Run` against fixture hooks under `OPAGACHI_HOME`; run elevated only if the sandbox prevents hidden Edge decoding). Captures: `tools/dev/captures/`, each in Dark, `-light` and `-larger` (text size Larger). No prompts are sent to agent apps by this harness; reset tests touch scratch assets and fixture hooks only.
