# Terminal Commands Cheat Sheet (pwsh-first)

- Default shell: PowerShell (pwsh). Detect before running commands.
- Web fetch: Use PowerShell Invoke-WebRequest (or curl) — web-fetch tool is disabled.
- Prefer automation via MCP tools, terminal commands next, then GitHub API calls last.

## PowerShell basics

- Current shell: `$PSVersionTable.PSEdition`, `$PSVersionTable.PSVersion`
- Download raw file: `Invoke-WebRequest -Uri <raw-url> -OutFile <path>`
- Parallel downloads: `ForEach-Object -Parallel` (PowerShell 7+)
- JSON: `Get-Content -Raw file.json | ConvertFrom-Json`

## GitHub CLI (gh)

- Auth: `gh auth status`
- Repo: `gh repo create owner/name --public|--private --template nam20485/ai-new-app-template`
- View: `gh repo view owner/name -w`
- Issues: `gh issue create -t "Title" -b "Body"`
- Subissue: `gh issue create --title "Subissue Title" --body "Details" --parent 123`
- PR: `gh pr create --title "Title" --body "Body" --base

## Git

- Clone: `git clone https://github.com/owner/name.git`
- Branch: `git checkout -B main`
- Commit: `git add -A; git commit -m "msg"; git push -u origin main`

## Safety

- Use `-WhatIf`/`-Confirm` for destructive cmdlets.
- Quote paths with `-LiteralPath` and use `Join-Path`.

## Terminal/CLI Command/arguments Errors

- Check error code of last command: `$LASTEXITCODE`
- If failed, **BEFORE TRYING AGAIN:**
  - Check error message, and its context, for hints.
  - Verify command syntax with `--help` or `Get-Help`.
  - Inspect --help usage synatx and examples, and create a correct command based on thinking the usage through carefully.
  - If the command is complex, break it down into smaller parts and test each part before combining them.
  - If it still fails, search web for the error message and context to find solutions.
- DO NOT KEEP TRYING THE SAME COMMAND WITHOUT UNDERSTANDING THE ISSUE.
  - When you get an error, use the above steps to diagnose and fix it **BEFORE** retrying.
  - DO NOT just keep retrying UNLESS you know what the exact problem is and have fixed it, or are attempting fixes based on understanding the issue.
  - If you can't figure it out within three (3) tries, search the internet for documentation and/or solutions to the problem you are encountering.

Finally, once you have a working command, document it clearly for future reference in this document in the ##Examples section with a short description of what it does, and what problems you encountered before getting it to work.

---
This file exists to satisfy local docs linking and to standardize terminal usage when automation tools are unavailable.
