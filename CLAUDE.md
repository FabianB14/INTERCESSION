# SESSION — Project Context

Co-op horror escape room. 2–4 players. Steam PC, Early Access.
Studio: Interverse Corp.

Read `docs/LORE.md` before writing any content, naming, or dialogue.

---

## Stack

- **Unity 6 LTS**, Universal Render Pipeline (Forward+)
- **Netcode for GameObjects** + **Facepunch.Steamworks** (Steam relay transport)
- **Steam Voice** for proximity chat (decided 2026-08-08, over Vivox — no extra service dependency, no per-seat cost, and Steam is already a hard dependency). Falloff rules live in `Session.Core.Voice` and are unit tested; Steam is only the transport.
- Input System (not legacy Input)
- C# 9, nullable enabled in `Session.Core`

Target: 1080p / 60fps on a GTX 1060. If a feature can't hold that, it doesn't ship.

---

## Golden rules

These are hard constraints. Violating them costs hours of manual cleanup.

1. **Never hand-edit `.unity`, `.prefab`, or `.asset` YAML.** If scene or prefab changes are needed, either write an Editor script under `Session.Editor` that performs the change, or stop and tell me what to wire by hand. Describe it as a checklist I can follow in 60 seconds.
2. **Never create, edit, delete, or rename `.meta` files.** GUID breakage silently destroys asset references.
3. **Never rename or move assets** without telling me first — same reason.
4. **All gameplay logic lives in `Session.Core`** as plain C# with no `UnityEngine` dependency. MonoBehaviours are thin adapters that read input and push state. If you're writing an `if` statement about puzzle rules inside a MonoBehaviour, it's in the wrong assembly.
5. **Server is authoritative for all puzzle and door state.** Clients are authoritative for their own movement and look direction only, with server-side sanity checks on position delta. Never let a client tell the server a puzzle is solved.
6. **No allocations in `Update`, `FixedUpdate`, or `LateUpdate`.** No LINQ, no `foreach` over interfaces, no string concatenation, no `GetComponent`. Cache in `Awake`.
7. **Every new rule, timing value, or tuning number goes in a ScriptableObject**, never a hardcoded literal. I need to tune without recompiling.
8. **Write the EditMode test first** for anything in `Session.Core`.

---

## Assembly structure

```
Assets/
  Scripts/
    Core/        Session.Core.asmdef        (no UnityEngine ref — pure C#)
    Runtime/     Session.Runtime.asmdef     (MonoBehaviours, NGO, references Core)
    UI/          Session.UI.asmdef          (references Core + Runtime)
    Editor/      Session.Editor.asmdef      (editor tools, references all)
  Tests/
    EditMode/    Session.Tests.Core.asmdef  (references Core only — runs fast, no Editor needed)
    PlayMode/    Session.Tests.Runtime.asmdef
```

`Session.Core` compiling without `UnityEngine` is the single most important invariant in this project. It means puzzle logic, the perception system, layout generation, and the Attendant state machine are all testable in milliseconds without opening the Editor. Protect it.

---

## The Perception System (core mechanic)

Two players in the same room see different objects. This is the game.

- The server holds one **canonical room state**: `RoomId`, a set of `PropId`s, and a `PuzzleGraph` of preconditions and unlock states.
- Each player has a **Lens**: a deterministic mapping from `PropId` → `PropVariant`. The variant changes what the prop *looks like, is called, and reads as* — never what it *does*.
- **Solutions are canonical, descriptions are per-player.** A four-digit code is the same four digits for everyone; one player finds it stamped on a medication bottle, another finds it scratched into a bedframe.
- Lens assignment is seeded per-session per-player and lives in `Session.Core`. It must be pure and deterministic — same seed, same lens, every time. This is testable and must be tested.

Design consequence to respect: **never generate a lens pairing where one player can solve their own room alone.** Every lens must be missing at least one input another lens holds. Write a validator for this in `Session.Core` and run it in tests over thousands of seeds.

---

## The Attendant

Rule-based, not random. Players must be able to learn it.

- Deterministic finite state machine in `Session.Core`. States: `Dormant`, `Observing`, `Approaching`, `Enforcing`, `Withdrawing`.
- It escalates on **protocol violations**, not on noise: leaving a room with an unfinished puzzle, backtracking through a completed room, forcing a door, or exceeding the room's time allowance.
- It never spawns randomly and never teleports. If it's in the hallway, it walked there, and it can be heard walking there.
- All thresholds live in `AttendantProfileSO`.

---

## Commands

```bash
# EditMode tests (fast, primary loop — run these before every commit)
Unity -batchmode -quit -projectPath . -runTests -testPlatform EditMode -logFile -

# PlayMode tests
Unity -batchmode -quit -projectPath . -runTests -testPlatform PlayMode -logFile -

# Build
Unity -batchmode -quit -projectPath . -executeMethod Session.Editor.BuildPipeline.BuildWindows64 -logFile -
```

Run EditMode tests yourself after every change to `Session.Core`. Do not ask me to click Play to verify something a test could prove.

---

## Naming

- Namespaces mirror folders: `Session.Core.Puzzles`, `Session.Runtime.Networking`
- ScriptableObjects end in `SO`: `RoomLayoutSO`, `AttendantProfileSO`
- Networked MonoBehaviours end in `NetBehaviour`
- Interfaces prefixed `I`; no `Manager` in any type name — say what it does
- Prefabs: `PRE_`, materials: `MAT_`, ScriptableObject assets: `SO_`

---

## Art constraints (enforce in code review)

- Grounded-stylized: correct proportions, real material behavior, simplified surface detail. No photoscans.
- Palette is locked: mustard, oxide red, olive, cream, oak veneer, institutional green.
- **The accent color `#FF8A3D` means "interactable" and is never used decoratively.** Flag any material or UI element that uses it otherwise.
- Baked lighting plus a small realtime budget. Max 4 realtime lights per room, one of which is the player flashlight.

---

## Definition of done

A task is done when: EditMode tests pass, no new allocations in the per-frame profile, it works with 4 clients in a local multiplayer test, and no new warnings in the console. Not before.

---

## When to stop and ask me

- Anything requiring scene or prefab wiring
- Adding a package or third-party dependency
- Changing the network authority model
- Anything touching Steamworks or store configuration
- Any design decision about *feel* — timing, pacing, how frightening something is. That's mine.
