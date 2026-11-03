# Security policy

Please **don't open a public issue** for a security problem. Report it privately instead: on this repository's
**Security** tab, choose **Report a vulnerability**. Only the maintainer can see the report.

Useful details: the OpaGachi version (tray tooltip or Settings footer), Windows version, and steps to reproduce.

## How OpaGachi limits exposure
- No network access beyond this PC: it listens only on `127.0.0.1` for agent hooks.
- Requests that come from web pages are refused: any request with a browser `Origin` header, or with a `Host`
  other than `127.0.0.1` or `localhost` (DNS rebinding).
- It edits agent settings files only when you add or remove hooks, keeps a backup first, and only removes its
  own entries.
- It installs per user, without admin rights.
