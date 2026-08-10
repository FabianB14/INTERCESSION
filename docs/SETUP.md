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
   (`com.unity.netcode.gameobjects`). The code uses the universal `[Rpc(SendTo.…)]` attribute
   rather than the older `[ServerRpc]`/`[ClientRpc]` pair, so it needs **1.8 or newer** — that
   includes every 2.x release. Whatever Package Manager offers on Unity 6 will be fine.

   Careful: the Editor also ships a built-in package called `com.unity.netcode`. That is Netcode
   for **Entities**, a completely different product, and it is not what this project uses.
2. **Facepunch transport** — Package Manager → **+** → *Add package from git URL*:
   ```
   https://github.com/Unity-Technologies/multiplayer-community-contributions.git?path=/Transports/com.community.netcode.transport.facepunch
   ```
   This bundles the Facepunch.Steamworks DLLs and `steam_api64.dll`, so **do not** also install
   Facepunch.Steamworks separately — two copies of the same assembly is a guaranteed conflict.

The transport's assembly is named `Facepunch Transport for Netcode for GameObjects` — with spaces.
That exact string is what `Session.Steam.asmdef` references; it is not a typo.

### API audit status

The Unity-facing assemblies have never been compiled — there is no project to compile them in.
Every external API they call has been checked against the real source instead:

| Verified against | Covers |
|---|---|
| NGO source (`develop`) | `[Rpc]`/`SendTo`/`RpcParams`/`RpcTarget.Single`, `NetworkVariable` ctor and `OnValueChanged`, `CustomMessagingManager`, `FastBufferWriter`/`Reader`, the `NetworkManager` and `NetworkBehaviour` members used |
| Facepunch.Steamworks source | `SteamClient`, `SteamMatchmaking` events and async lobby calls, `Lobby` methods, `SteamFriends`, the `SteamUser` voice API |
| TextMeshPro in this Editor install | `TMP_Text.SetCharArray(char[], int, int)` |

That is not the same as compiling. Expect some fixes on first import — most likely in areas no
signature check can cover, like NGO's codegen constraints on RPC methods.

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
- **Create > Session > UI Palette** → `SO_UiPalette`
- **Create > Session > Content Table** → `SO_ContentTable`
- **Create > Session > Room Layout** → `SO_Room09`
- **Create > Session > Session Catalog** → `SO_SessionCatalog`, then drag the tuning assets into it

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

## 4c. UI wiring

6. **Lobby canvas**: `LobbyView` with the host/invite/ready/start/leave buttons and four slot
   groups. Put `LobbyUiBinder` beside it and point it at the view.
7. **HUD canvas** (screen space): `InteractionPromptView` (CanvasGroup + TMP label),
   `SessionLogView` (CanvasGroup + TMP label), `VoiceIndicatorView` (mic icon + four speaker
   lights). Assign `SO_UiPalette` and `SO_ContentTable` to each.
8. **`SessionHudBinder`** on the HUD root: point it at the log and the room's keypad, and set the
   room/node numbers the keypad belongs to.
9. **Keypad prefab** (world space): `KeypadView` with digit buttons, backspace, submit and a TMP
   readout. Its code length must match that node's solution length in the `RoomLayoutSO`.
10. **Reader canvas**: `DocumentReaderView` — CanvasGroup, title/body/page TMP labels, a paper
    Image, and next/previous/close buttons. Assign `SO_ContentTable` and `SO_UiPalette`.
    Hook `ReadingStarted`/`ReadingEnded` to slow the player rig — **do not pause anything**. The
    Attendant keeps walking and the room's allowance keeps running while you read; that is the
    point of paper.
11. **Paper props**: `PaperPropView` beside the prop's `PropView`, with one `DocumentSO` per
    variant **in the same order as the `RoomLayoutSO`'s variant list**.
12. **Tape deck prefab**: `TapeDeckNetBehaviour` + `NetworkObject` + `AudioSource` (3D, spatial
    blend 1). Assign an `SO_Tape`. Wire the deck's interaction to `TogglePlayRpc`.
13. **Transcript overlay** on the HUD canvas: `TranscriptView` (CanvasGroup, line label,
    attribution label). Put `TapeBinder` on the HUD root, point it at the transcript, and list
    every deck in the scene.

### Authoring a paper prop

A document is a prop *variant*, so a clue-carrying paper prop needs **two genuinely different
documents**, not one document with a hole in it. One player has the admission form with the ward
number on it; the other has the discharge checklist — a real, complete, honest document that simply
never mentions it. `Session > Validate Paper Props` warns when a concealing variant is just the
revealing one with a line hidden.

Mark the line carrying the puzzle input as **ClueBearing**. Everything else stays `Body`,
`Heading`, `Footer`, or `Struck`. Verity's Three Principles are appended automatically to every
page from the document's footer key.

### Copy you need to write

`SO_ContentTable` starts empty. The keys the UI looks for by default:

| Key | Suggested copy |
|---|---|
| `ui.verb.examine` / `.use` / `.read` / `.open` | Examine / Use / Read / Open |
| `ui.log.room_complete` | "This room is complete. You may proceed from" |
| `ui.log.left_unfinished` | "No room may be left unfinished." |
| `ui.log.overrun` | "The room is patient. Please continue in" |

Those are placeholders in the Institute's register, not final copy. Per LORE.md the building is
never threatening in its own voice — the dread is the gap between how politely it speaks and what
it is doing. Copy is a design call, so it is yours.

## 5b. Content validators

Run all three before any content commit:

| Menu item | Catches |
|---|---|
| `Session > Validate Room Layouts` | A room one player could solve alone, over 2000 seeds × 2–4 players |
| `Session > Validate Accent Colour Use` | #FF8A3D on anything that is not interactable |
| `Session > Validate Paper Props` | A concealing document that spells out the answer anyway |
| `Session > Validate Intake Tapes` | A tape that speaks an answer, duplicate tape ids, missing or thin transcripts |

The third is the one to run most. Write the withheld version of a patient file, mark the obvious
line as ClueBearing, and forget the same four digits appear two paragraphs down as a ward
number — the room still gets solved, so nothing *looks* broken. The player who can read it has no
idea their partner was supposed to be needed. It is invisible in a play test and fatal to the
premise.

It is a literal-match check: it finds "4172", "4-1-7-2" and "4 1 7 2", and it will **not** find the
answer paraphrased ("the year the annex opened"). A human still has to read the copy.

The tape check is the same idea with a sharper edge. A document *can* carry a clue, because each
player reads a different document. **A tape cannot** — it is a recording, so everyone hears the
same words. An answer spoken on tape does not leak to one player; it hands the whole group the
same clue, and that room instantly needs no co-operation from anybody. Nobody involved experiences
anything odd, so a play test reads it as the room being easy rather than as the central mechanic
switching itself off. Every tape is checked against every room's solution, since decks move and
rooms get reordered.

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
| `Session.UI` | Views and presenters | References only Core + Runtime + uGUI/TMP, per CLAUDE.md. Knows nothing about NGO or Steam, so lobby and HUD layout can be iterated on before either package finishes importing |
| `Session.Netcode` | All `NetBehaviour` adapters, HUD binder | Needs NGO; platform-agnostic |
| `Session.Steam` | Lobby, transport glue, voice, lobby binder | The Facepunch transport restricts itself to Editor + standalone platforms, so anything referencing it must too. Folding this into Runtime would drag that constraint across the whole game |

UI uses **uGUI + TextMeshPro** rather than UI Toolkit. Most of this game's UI is diegetic and
world-space — keypads on walls, prompts on props — and that is uGUI's strength. Full-screen menus
would be nicer in UI Toolkit; mixing both was not worth the second system.

Say the word and it collapses back to two assemblies; the split is a judgement call, not a
requirement.

## What is not built yet

**Scene content.** No scenes, prefabs, or materials — `.unity`/`.prefab`/`.asset` YAML is hand-off
territory until MCP is connected. Section 4b is that hand-off.

**UI art.** The views exist and are wired to Core; no layouts, sprites, fonts or copy do. The
palette asset holds placeholder values for the five body colours — only the accent is a real,
locked value.

**The forty-one names.** The reader exists; the files do not. LORE.md wants forty-one intake names
used consistently across room details, plus the 1979–1984 staff-memo drift. That is writing work,
and it is the cheapest story-per-pound in the project.

**The tapes themselves.** The deck, sync, transcript and audit all exist; there is no recorded
audio and no written dialogue. LORE.md ranks this as the single highest story-per-pound asset in
the project — one VO actor, a good mic, a room's worth of blankets. Verity is warm, unhurried, and
faintly apologetic about the paperwork, and should be the most likeable thing in the game.

**The Overrun.** Held back deliberately per LORE.md as the Early Access → 1.0 hook. Nothing in the
codebase references it, which is correct for now.

**Voice tuning under load.** The routing logic is unit tested, but per-frame allocation and
bandwidth with four speakers has not been profiled — that needs four real clients and the Unity
profiler, which is a play test, not a unit test.
