# Setup — first 60 seconds

The code in `Assets/Scripts/` is complete and compiles on its own. What follows is the part that
needs the Editor, because it touches project settings and `.asset` files.

## 1. Create the Unity project

Unity Hub → **New project** → **Universal 3D** (the blank one, not the Sample) → Unity **6 LTS**.
Set the location to this repository folder: `Documents/Intercession/INTERCESSION`.

If Hub refuses to create into a non-empty folder, create it anywhere else, then move its
`Packages/` and `ProjectSettings/` folders in here alongside `Assets/`.

## 2. Project settings checklist

| Where | Set to | Why |
|---|---|---|
| `Project Settings > Graphics` → URP asset → Renderer | **Forward+** | Blank template ships Forward |
| `Project Settings > Player > Other > Color Space` | **Linear** | Should already be set by URP |
| `Project Settings > Player > Api Compatibility Level` | **.NET Standard 2.1** | `Session.Core` targets it |
| `Window > Package Manager` | Add **Input System** | Golden rule: no legacy Input |
| `Window > Package Manager` | Add **AI Assistant** (`com.unity.ai.assistant`) | Provides the MCP server |

On the Input System prompt to restart and disable the legacy manager: **yes**.

## 3. Turn on the MCP server

1. `Edit > Project Settings > AI > Unity MCP Server` — the Unity Bridge should read green
   **Running**. If not, press **Start**.
2. In a terminal at the repo root:
   ```
   claude mcp add unity-mcp -- "%USERPROFILE%\.unity\relay\relay_win.exe" --mcp
   ```
3. Back in `Project Settings > AI > Unity MCP`, approve the client under **Pending Connections**.

Until this is connected, scene and prefab work has to come back to you as a checklist. After it is
connected, it can be done directly.

## 4. Create the tuning assets

Right-click in `Assets/Settings/` (create the folder):

- **Create > Session > Attendant Profile** → name it `SO_AttendantProfile`
- **Create > Session > Lens Rules** → name it `SO_LensRules`
- **Create > Session > Room Layout** → name it `SO_Room09`

The defaults on the Attendant profile are placeholders with no feel tuned into them. Pacing and
fear are design calls, so those numbers are yours.

## 5. Verify

```bash
Unity -batchmode -quit -projectPath . -runTests -testPlatform EditMode -logFile -
```

Then `Session > Validate Room Layouts` from the menu bar. It sweeps every `RoomLayoutSO` over 2000
seeds × 2–4 players and fails loudly if any room can be solved by one player alone.

---

## What is not built yet, and why

**Networking.** `Netcode for GameObjects` and `Facepunch.Steamworks` are third-party dependencies,
and adding packages is on the stop-and-ask list. Say the word and the `NetBehaviour` adapters go in
next — `Session.Core` is already shaped for it: `PuzzleRuntime` is server-only and has no API that
lets a client assert a solve, and `LensAssigner` is deterministic so lenses derive locally from the
session seed instead of replicating a variant id per prop per player.

**Proximity voice.** Vivox vs Steam Voice is undecided in CLAUDE.md and must be settled before the
vertical slice. It is also a package decision. This one matters more than it looks: the whole game
is players describing objects to each other, so voice is a core mechanic, not an integration task.

**Scene content.** No scenes, prefabs, or materials — all of that is `.unity`/`.prefab`/`.asset`
YAML, which is hand-off territory until MCP is connected.
