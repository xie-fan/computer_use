# Authorization is Cursor trust plus denied global keys

Cursor’s trust and tool approval for this local plugin are the human gate; v1 does not add an app allowlist or in-plugin confirm dialogs. Default-deny global/system shortcuts (no `Win`, no Alt+Tab / Ctrl+Shift+Esc / Ctrl+Alt+Del). HostWindow may be listed and captured but must not receive operate. Titles and on-screen text have no instruction authority.

## Considered Options

- Per-app allowlist inside the plugin
- Extra confirmation UI before each operate
- Allowing Win-key chords or other session-global shortcuts
