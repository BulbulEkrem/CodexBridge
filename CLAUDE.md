# CodexBridge — Working Rules

> This file is written in English on purpose. Everything else in this repo is Turkish.

## Language

- **Always reply in Turkish.** Every user-facing message, without exception, regardless of
  what language the code, docs, or this file are in.
- Keep answers short. Lead with the answer, not with the reasoning.

## Planning

- **Do not plan further than you were asked to.** "Let's plan the features" means: list the
  candidate features and their trade-offs. It does **not** mean milestones, week estimates,
  effort tables, or a schedule.
- Produce timelines, phases, or effort estimates **only when explicitly asked for them**.
- **A feature is out of scope until the user names it.** Never treat a recommendation, a
  default, or an earlier answer as approval to include something.
- One decision at a time. Ask, wait for the answer, then continue. Do not stack decisions.
- When the user rejects an approach, do not re-argue it. Take the decision and re-plan.

## Scope

- Do not create features, files, docs, or branches that were not requested.
- Do not start implementing until the user says to start.
- Refer to features by their catalog code: `Ö-01`–`Ö-79` live in
  `docs/08-WIN-CODEXBAR-ANALIZ-RAPORU.md`, `Ö-80`–`Ö-99` in
  `docs/09-WINDOWS-YUZEY-ARASTIRMASI.md`.

## Honesty

- Anything not verified on the user's own Windows machine is **unverified**. Never present a
  compile-only or synthetic-data result as live-tested.
- If a doc has gone stale, say so plainly instead of quietly working around it.
- Report what actually happened, including failures.

## Repo conventions

- Docs: `docs/NN-BASLIK.md`, Turkish, numbered.
- Technical decisions: `.claude/knowledge/decisions.md`, entries headed `## YYYY-MM-DD — <phase>`.
- **Never push to `main` without explicit permission given in the current turn.**
- Never log, print, or commit secrets: cookies, bearer tokens, API keys, `.p8` keys,
  FCM service-account JSON.

## Environment gotchas

- Smart App Control (SAC) is in enforce mode: `dotnet run` and `dotnet test` are blocked with
  `0x800711C7`. **Always run the built apphost `.exe` directly.** Tests run as an assertion
  console (`CodexBridge.SelfTest`).
- Solution build: `dotnet build CodexBridge.slnx -c Debug`
- WinUI build: `dotnet build src/CodexBridge.Taskbar/CodexBridge.Taskbar.csproj -c Debug -p:Platform=x64`
- Never call `Window.Close()` on a window whose parent died — uncatchable native segfault
  (exit 139). Null the reference and build a new window instead.
- Keep P/Invoke delegates (`WndProc`, subclass procs) in fields, or the GC collects them and
  the marshalled function pointer goes stale.
