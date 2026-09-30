# Claude memory snapshot

These are Claude Code's persistent notes about this project (architecture, design decisions, hard-won gotchas).
Claude reads them from your user profile, not from the repo — this folder is a backup so they travel with the project.

## Restore on a new computer

Claude keeps per-project memory in:

```
%USERPROFILE%\.claude\projects\<project-path-with-dashes>\memory\
```

For a checkout at `C:\Users\<you>\CLAY` that folder is `C:\Users\<you>\.claude\projects\C--Users-<you>-CLAY\memory\`.
Copy every `.md` file from here into it (create the folder if needed). If the project lives somewhere else, the
folder name is the project path with `:` and `\` turned into `-`.

Or just open Claude Code in the project and say: "restore your memory from Gameplan/ClaudeMemory".

Snapshot taken 2026-09-30.
