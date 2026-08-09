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

### Networking packages

`Session.Netcode` and `Session.Steam` will not compile until these are installed. Install them
before opening the project, or expect a wall of "assembly reference not found" errors on first
import — they clear as soon as the packages resolve.

1. **Netcode for GameObjects** — Package Manager → Unity Registry → *Netcode for GameObjects*
   (`com.unity.netcode.gameobjects`). NGO **2.x** is required; the code uses the universal
   `[Rpc(SendTo.…)]` attribute, not the older `[ServerRpc]`/`[ClientRpc]` pair.
2. **Facepunch transport** — Package Manager → **+** → *Add package from git URL*:
   ```
   https://github.com/Unity-Technologies/multiplayer-community-contributions.git?path=/Transports/com.community.netcode.transport.facepunch
   ```
   This bundles the Facepunch.Steamworks DLLs and `steam_api64.dll`, so **do not** also install
   Facepunch.Steamworks separately — two copies of the same assembly is a guaranteed conflict.

The transport's assembly is named `Facepunch Transport for Netcode for GameObjects` — with spaces.
That exact string is what `Session.Steam.asmdef` references; it is not a typo.

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

- **Create > Session > Attendant Profile** → `SO_AttendantProfile`
- **Create > Session > Lens Rules** → `SO_LensRules`
- **Create > Session > Movement Rules** → `SO_MovementRules`
- **Create > Session > Voice Rules** → `SO_VoiceRules`
- **Create > Session > Room Layout** → `SO_Room09`
- **Create > Session > Session Catalog** → `SO_SessionCatalog`, then drag the five above into it

The defaults on the Attendant profile are placeholders with no feel tuned into them. Pacing and
fear are design calls, so those numbers are yours.

## 4b. Scene wiring for multiplayer

Roughly a five-minute pass, and the one part I cannot do until MCP is connected:

1. Empty GameObject **`NetworkRig`** (root, mark `DontDestroyOnLoad` via the lobby service):
   - `NetworkManager` — set **Transport** to `FacepunchTransport`
   - `FacepunchTransport` — set **Steam App ID** (480 is fine until you have a real one)
   - `SteamLobbyService` — set the same App ID
   - `SessionDirectorNetBehaviour` — assign `SO_SessionCatalog`
   - `SteamVoiceRelay` — assign `SO_SessionCatalog`
   - Add a `NetworkObject` to this root, and register it in NetworkManager's **Network Prefabs**
2. Player prefab **`PRE_Player`**: `NetworkObject`, `CharacterController`,
   `PlayerMotorNetBehaviour`, `AudioSource` (3D, spatial blend 1) + `VoicePlayback`.
   Set it as NetworkManager's **Player Prefab**.
3. Attendant prefab **`PRE_Attendant`**: `NetworkObject`, `NavMeshAgent`,
   `AttendantNetBehaviour`. Fill the patrol route and one `RoomAnchor` per room (door anchor
   outside the doorway, interior anchor inside). Bake a NavMesh over the corridor and rooms.
4. Per room, an empty **`Room09`** with `PerceptionNetBehaviour`: assign `SO_SessionCatalog`, set
   the room number, and drag every `PropView` in the room into its list.
5. Each prop: `PropView` with its `PropId` matching the `RoomLayoutSO`, one child GameObject per
   variant **in the same order as the layout's variant list**, and the clue surface assigned.

Order matters in step 5. `PropView` maps variant index to child index positionally, and it logs an
error rather than guessing if the counts disagree.

## 5. Verify

```bash
Unity -batchmode -quit -projectPath . -runTests -testPlatform EditMode -logFile -
```

Then `Session > Validate Room Layouts` from the menu bar. It sweeps every `RoomLayoutSO` over 2000
seeds × 2–4 players and fails loudly if any room can be solved by one player alone.

---

## Assembly layout note

CLAUDE.md's structure puts NGO inside `Session.Runtime`. It ended up split three ways instead:

| Assembly | Contains | Why separate |
|---|---|---|
| `Session.Runtime` | ScriptableObjects, `PropView` | Compiles with **no** packages installed, so tuning assets and views stay editable even if networking is mid-upgrade |
| `Session.Netcode` | All `NetBehaviour` adapters | Needs NGO; platform-agnostic |
| `Session.Steam` | Lobby, transport glue, voice | The Facepunch transport restricts itself to Editor + standalone platforms, so anything referencing it must too. Folding this into Runtime would drag that constraint across the whole game |

Say the word and it collapses back to two assemblies; the split is a judgement call, not a
requirement.

## What is not built yet

**Scene content.** No scenes, prefabs, or materials — `.unity`/`.prefab`/`.asset` YAML is hand-off
territory until MCP is connected. Section 4b is that hand-off.

**UI.** `Session.UI` is an empty assembly. Lobby screens, the interaction prompt, and the
push-to-talk indicator all live there and none exist yet.

**Voice tuning under load.** The routing logic is unit tested, but per-frame allocation and
bandwidth with four speakers has not been profiled — that needs four real clients and the Unity
profiler, which is a play test, not a unit test.
