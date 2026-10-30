# Agent Guidelines

## Project context

- Penguise: multiplayer social deduction game inspired by Feign. Hidden roles, private information, discussion, voting, and round outcomes form intended gameplay. Do not invent roles, abilities, win conditions, or phase rules; confirm requirements before implementing them.
- Unity version: `ProjectSettings/ProjectVersion.txt` (currently 6000.6.3f1). Check this file instead of assuming latest Editor or package APIs.
- Networking: Unity Multiplayer Services (MPS), anonymous Authentication, Lobby, Relay, and Netcode for GameObjects (NGO). Room creator acts as host; host also counts as player. Room names need not be unique; join codes identify rooms.
- Room size: 4 minimum, 8 maximum. Lobby shows `n/8`; only host can start with 4–8 players. Validate start eligibility in logic as well as UI. Source: `Assets/Scripts/Multiplayer/RoomSessionManager.cs`.
- UI: UI Toolkit (UXML/USS), Unity Localization with English and Turkish. Update affected string tables alongside UI changes; preserve loading feedback, disabled controls during pending requests, and actionable error messages.
- Existing flow: anonymous sign-in, room creation/join by code, MainMenu/Lobby transitions, player list, room-code copying, and leaving. Lobby uses compact centered cards and scrollable player list; local player and host have badges.
- Gameplay loop remains unfinished. Player naming is deferred. Treat repository files as current implementation; another conversation may have changed them.

## Planned in-game text chat

- Decision, not implemented yet: use existing NGO + Relay connection. No Vivox or separate chat backend needed for current scope.
- Chat belongs to active match/hand, not lobby. Text only; no voice. Clear messages when match ends and before next hand. Keep bounded in-memory history; no persistent storage.
- Host validates sender identity, message length, and send rate before distributing messages to match participants.

## Web delivery

- Sibling repositories: this Unity game (`Penguise`) and Next.js host (`../website`), under GitHub organization `penguise`. Website deployed on Vercel at `https://penguise.odest.dev`.
- Release build output: `../website/public/penguise`. Website loads Unity directly into viewport-filling canvas. User already knows build/deploy workflow; do not re-explain unless asked.
- Website owns loader UI and HTTP headers. Download progress switches to spinner with `Preparing game…` at Unity's 90% stage. Native progress does not accurately represent combined data/WASM download progress.
- Unity files use `Cache-Control: public, max-age=0, must-revalidate`; unchanged files can reuse cache after validation, changed files download again. Do not introduce long-lived immutable caching for overwritten filenames.
- Recent compressed release size was about 12 MB; first builds took 1–1.5 hours. These are historical measurements, not fixed budgets. Reuse existing builds only when suitable for requested test.
- Builds in game repository stay ignored. Selected runtime output in website repository is intentionally tracked; its generated `index.html` and `TemplateData/` are ignored. Check actual ignore rules before staging.

## Solo multiplayer testing

- Developer usually tests alone, with 16 GB RAM and limited disk space. Four players remain necessary for full match-flow checks; reducing to two does not verify four-player gameplay.
- Multiplayer Play Mode supports main Editor plus three additional Editor instances. Current scenario lives under `Assets/Settings/PlayMode/`; inspect files instead of assuming scenario name.
- Observed with four instances: total system memory reached 97%; Unity process group used about 6.4 GB. Measure current resource use; do not promise comfortable operation or equate Editor memory with release-player memory.
- Additional instances share source assets on disk, but consume process memory and can create temporary/cache files. Avoid unnecessary project copies and builds.
- Compiled clients/browser sessions are alternatives when current build exists. Do not propose rebuilding after every edit. Multiple clients need distinct authentication identities.
- Solo tests can verify rules, synchronization, chat, and disconnect behavior. Social deception, pacing, fun, and role balance still need eventual human-group playtests.

## Collaboration and boundaries

- Discuss with user in Turkish; write code, identifiers, comments, and repository documentation in English. Localize player-facing UI through existing English/Turkish tables.
- Discussion or future intent is not authorization to edit. Implement when requested; do not turn questions into unsolicited changes. Never commit, push, or deploy without explicit authorization.
- For commit recommendations, inspect `git status` and full relevant `git diff`, not only summaries. Use Conventional Commit titles and group changes by meaningful purpose. Preserve unrelated work.
- Use applicable skills and current official documentation for platform/API decisions. Prefer existing project patterns and smallest complete change; distinguish verified behavior from assumptions.
- Services should remain within free-tier scope. Historical dashboard checks showed $0 reported cost; recheck rather than claiming current usage. Cloud Project ID handling is deferred; do not add quota-protection systems or reopen that topic without request.
- Keep this file concise: durable decisions, constraints, and pointers. Update implementation status when features land; avoid chat transcripts and stale task logs.

## UI blur on WebGL 2

- Do not use `backdrop-filter` for the room dialog background. In this project, Unity 6000.6.3f1 rendered it correctly in the Editor (Direct3D12) but produced a black background in the deployed WebGL 2 build.
- Relevant Unity issue: [UUM-155203](https://issuetracker.unity.com/issues/25524/ui-toolkit-backdrop-filter-does-not-render-the-content-behind-the-element-on-webgl-2). The report describes dark/empty backdrop output on WebGL 2 and notes that ordinary `filter: blur()` works.
- Preserve the verified workaround in `Assets/UI/MainMenu/MainMenu.uss` and `Assets/Scripts/UI/RoomDialogController.cs`: apply the `room-dialog-blurred` class (`filter: blur(5px)`) to `menu-content` and `language-panel` when the dialog opens; remove it when the dialog closes.
- Keep the dialog outside the blurred elements. Keep the overlay's semi-transparent background separate from the blur. Do not apply the blur class to the overlay or the entire UI root.
- The current camera background is a solid color. Filtering the menu elements is sufficient for this screen; it does not blur camera-rendered scene content.
- The user confirmed this workaround fixed both the blur and black background in the deployed browser build on October 30, 2026. Editor compilation alone does not verify WebGL rendering.

## Validation and builds

- Prefer script compilation and Console checks for routine validation. Do not run a player/Web build unless the user explicitly requests one; Web builds are expensive in this project.
- Source changes do not update an existing web build. Browser verification requires a new Unity build and deployment, which must be reported separately from Editor checks.
