# Rivage autosplitter reference

`Rivage.asl` provides these signals for the verified Steam build:

- Start Real Time (RTA) and Game Time (LRT) when New Game's initial loading screen
  closes, at the beginning of the introduction. The initial load is excluded from
  both timing methods.
- Optionally enable **Start: After Cutscene** to start both timing methods when the
  second New Game loading screen closes, after the introduction or its skip.
- Pause Game Time while the actual loading screen is active, including the load
  after skipping the introduction. Resume as soon as that screen closes. All menu
  navigation, logos and the skippable flashing-lights warning remain timed, as do
  gameplay and cinematics outside loading screens.
- Split once when the final Pod's opening sequence starts playing. Unlocking the
  Pod does not split.
- Optionally split when each selected level card or access card is added to the
  player's inventory.
- Optionally split when all four matching Jonny fingerprint fragments are owned.
- Optionally split when the Lobby's decoded map displays its drawing, or when
  the Laboratory gear-puzzle chest's sequence drawing is clicked for inspection.
- Optionally split when the Horizon probe projects its code on the wall after
  the six-note pad sequence.

Card, fingerprint, drawing and projection splits are disabled by default.
There are no automatic resets or category options.

## Setup

1. Add **Control → Scriptable Auto Splitter** in LiveSplit's **Edit Layout**,
   then select `Rivage.asl` in the component settings.
2. Select **Game Time** to display time with loading removed.
3. Enable **Start** and **Split** in the component settings before starting a run.
4. Enable the individual card, fingerprint, drawing and projection splits you want, then add matching
   segments in your route order followed by the final Pod segment. With no pickup
   options enabled, use one final segment or advance intermediate segments manually.
5. Leave the script attached at the main menu before selecting New Game.

Save the layout to retain the script path and settings. After replacing the
script, reload it in LiveSplit with the timer reset to apply the new source.

Continue and attachment during an existing game do not start the timer. Attaching
during the final sequence, including while it is paused, does not split
retrospectively. The final signal advances the current segment; it does not skip
any remaining segments. Reset attempts manually.

## Start timing

**Start: After Cutscene** is the first script setting and is disabled by default:

- Disabled: start RTA and Game Time after the first New Game loading screen,
  at the beginning of the introduction.
- Enabled: start both after the second loading screen, following the introduction
  or its skip. The introduction and both initial loads are excluded.

Choose the setting before selecting New Game. The script captures this choice
when New Game is detected, then counts completed MoviePlayer loading screens.
Each screen counts once when its active state becomes inactive; repeated samples
during a screen or the introduction do not increment the count. This option
starts the timer; it does not advance a segment. Subsequent loading screens
continue to pause only Game Time.

## Optional card splits

The eight card checkboxes follow the start setting in this order. Each is
independent: for example, Pod
Bay Access can be selected while the Level 1 card split is disabled.

| Setting | Inventory row |
| --- | --- |
| Level 1 - Miranda card | `MirandaCard` |
| Pod Bay Access | `PodPass` |
| Level 2 - Amaan card | `AmaanCard` |
| Multipass | `Multipass` |
| Level 3 - Jahi card | `JahiCard` |
| Train Card (Jahi) | `TrainCardJahi` |
| Level 4 - Rafael card | `RafaelCard` |
| Level 5 - Jonny card | `JonnyCard` |

Configure the checkboxes before the attempt. Each selected card splits once on
its addition to the active inventory, regardless of the order in which you collect
cards. Opening the inventory, displaying a card, receiving another item, or
removing and reacquiring the same card cannot produce another split. Existing
cards on attachment or after loading a save establish a baseline without splitting.
New Game rearms the card signals for the next attempt.

The game's save data and item table were inspected to identify these eight row
names. The script reads the live inventory, not saved progress or localized text.
LiveSplit's **Split** checkbox still applies. The final Pod opening
takes priority if it coincides with a card event.

## Optional Jonny fingerprint split

**Jonny fingerprint (4/4)** appears after the eight card settings and is disabled
by default. It splits once when an observed incomplete fingerprint becomes
complete with all four distinct Jonny fragments, regardless of collection order.
The first three fragments and fragments belonging to other crew members do not
split. The fingerprint and Jonny's level card are separate options.

An already complete fingerprint on attachment, after loading, or after an
unreadable or replaced inventory establishes a baseline without splitting.
Replacing fragments and completing the fingerprint again cannot duplicate the
split. New Game rearms it for the next attempt. LiveSplit's **Split** checkbox
still applies, and the final Pod opening takes priority. A simultaneous
card pickup and fingerprint completion emit one split each on consecutive ticks.

## Optional drawing and projection splits

Three independent settings follow the fingerprint option:

| Setting | Trigger |
| --- | --- |
| Lobby decoded map | The decoded map switches from the puzzle to its drawing view |
| Laboratory sequence drawing | The player clicks the sequence drawing inside the gear-puzzle chest to inspect it |
| Horizon probe projection | The six-note pad sequence makes the probe's code projection visible on the wall |

The Lobby option does not split on code validation while reconstruction is still
running. The Laboratory option does not split on puzzle resolution, chest arrival
or chest opening, and does not wait for the drawing's inspection animation.
The Horizon option follows the projection's actual visibility, independently of
the puzzle's resolved flag. Attach the script before inspecting and playing the
pad near the probe. Replaying the sequence cannot duplicate the split.

Each drawing or projection splits once per attempt. Attachment with its display already active,
reader recovery, replaced objects and restored loading/menu states establish a
baseline without splitting. Reopening the drawing does not duplicate its split.
New Game rearms all three options. Configure options before starting the attempt and
add their segments in your route order. LiveSplit's **Split** checkbox applies.
The final Pod split takes priority; simultaneous drawing and pickup events are
emitted on consecutive ticks.

The Lobby reader follows the local pawn's CurrentWidget to the open PC and its
decoded-map view, independently of cursor position. It uses bounded pointer reads
and never scans the global object array or level actors. Loading, menus, closed
PCs and invalid map state invalidate the baseline. Display readers are idle when
their option is disabled; their read failures do not pause Game Time.

The Horizon reader discovers the probe through the inspected pad's `OnSoundPlay`
delegate bound to `SoundPlayed`. Each delegate supplies an object index and serial;
the reader resolves only that referenced object and validates its class and serial.
It retains the reference after the pad closes and checks the wall projection's
visibility and ownership. Discovery reads at most sixteen delegates, and normal
polling never scans the object array or level actors. Menus, loading, replaced
pawns and disabled settings discard the reference.

## Supported executable

- Process: `Rivage-Win64-Shipping.exe`
- Store: Steam, installation directory `Project Singularity`
- Unreal build: `++UE5+Release-5.7-CL-51494982`
- Image size: `164438016` bytes
- SHA-256: `2AEFBF42C85A52F241BD1ADC08ADE906267A6CF45F1E54CBED1888C05DEBC553`

Both image size and executable hash are checked on attachment. Unknown builds are
rejected with an explicit error. Addresses are module-relative and resolved again
after the process restarts. No process writes, hooks or injected code are used.

## Memory signals

All offsets below are hexadecimal. `GEngine` is at executable + `0x939A830`, and
`GameEngine + 0x12C8` points to `GlobalInstance_C`.

| Owner | Offset | Signal |
| --- | --- | --- |
| GlobalInstance | `0x263` | Native transition flag used to arm New Game detection; has a two-second tail |
| GlobalInstance | `0x3C0` | `IsMainMenu` |
| GlobalInstance | `0x2E8` | `BPSaveGame` |
| BP_SaveGame | `0x28` | `NbrLoopSave`, initially `-1` for New Game |
| BP_SaveGame | `0x40` | `FirstSaveDone`, initially false for New Game |
| GlobalInstance | `0x38` | LocalPlayers array data pointer |
| First local player | `0x30` | PlayerController |
| PlayerController | `0x2F0` | Local `CharacterFPS_C` pawn |
| CharacterFPS | `0x6B0` | Local `AC_Inventory_C` component |
| CharacterFPS | `0x7B0` | CurrentWidget, validated as `W_RafaelLaptop_C` |
| W_RafaelLaptop | `0x390` | `W_RafaelLaptop_Hexadecimal_C` map widget |
| Hexadecimal map widget | `0x330` | WidgetSwitcher |
| Hexadecimal map widget | `0x398` | `W_RafaelDrawing_Cubik_Where_C` drawing |
| WidgetSwitcher | `0x190` | ActiveWidgetIndex, `1` for the decoded drawing |
| CharacterFPS | `0x708` | CurrentInspectedAC, selected on click |
| AC_Inspectable | `0x2D0` | Owner, validated as `BP_VaultLab_Rubik_Sequence_C` |
| Laboratory sequence drawing | `0x2B8` | AC_Inspectable, validated against the local pawn's selected component |
| StellarWave pad | `0x3E8` | OnSoundPlay multicast delegate array, 16-byte bindings |
| Executable | `0x920CB10` | Global object array, used only for indexed delegate references |
| Horizon showcase | `0x2E0` | Projection StaticMeshComponent |
| Projection | `0x1A8` | bVisible, bit mask `0x20` |
| Projection | `0x1A9` | bHiddenInGame, bit mask `0x08` |
| AC_Inventory | `0xF0` | Owning `CharacterFPS_C`, validated against the local pawn |
| AC_Inventory | `0xF8` | `Inventory` array: data pointer, count, capacity |
| AC_Inventory | `0x120` | `fingerprint` array of four `FDataTableRowHandle` slots |
| CharacterFPS | `0x81B` | `CurrentCinematic`, `2` for the Pod ending |
| CharacterFPS | `0x778` | `CinematicActor` |
| LevelSequenceActor | `0x2F8` | `LevelSequenceAsset`, named `Cinematic_End` |
| LevelSequenceActor | `0x2F0` | SequencePlayer |
| SequencePlayer | `0x290` | Playback status, `1` for Playing |
| Executable | `0x93533E8` | Default movie player singleton pointer |
| Default movie player | `0x10` | `IGameMoviePlayer` vtable, executable + `0x7709050` |
| Default movie player | `0xA0` | Loading-screen thread pointer, cleared before the final on-screen wait |
| Default movie player | `0xB0` | Playback-active byte, includes the final wait and viewport restoration |

The Unreal name pool is at executable + `0x913EA80`. Readers validate relevant
object classes and byte ranges. Native disassembly confirms the private transition
byte at `0x263`. `EndLoadingScreen` schedules its clearing callback two seconds
later; this flag must therefore not control Game Time.

Load removal follows the movie player's loading-thread pointer together with its
playback-active byte. The
[`IsMovieCurrentlyPlaying`](https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/MoviePlayer/IGameMoviePlayer/IsMovieCurrentlyPlaying?application_version=5.5)
getter only tests the loading-thread pointer at singleton + `0xA0`.
`WaitForMovieToFinish` destroys that thread before continuing to display the
loading screen on the game thread, so the getter alone resumes Game Time too early.
The playback-active byte at singleton + `0xB0` is set by `PlayMovie` and cleared
after the final wait and removal of the loading viewport content. The reader
samples both fields together, validates the byte and interface vtable, and rechecks
the singleton pointer. It resumes on the first sample where both fields are
inactive, with no extra delay or debounce. The menu's skippable logo and
warning presentation uses the separate `W_LogoScreen_C` widget. It is not a
MoviePlayer loading screen. Actual loading screens still count as loading even
when file IO has finished but the screen remains displayed.

Starting requires an observed main menu followed by a fresh New Game transition,
using the original native flag and fresh-save check. This arms a pending start;
the script then requires an active MoviePlayer loading screen and starts on the
first readable sample where both loading-screen fields are inactive, or after
the second such screen when **Start: After Cutscene** is enabled. The screen
may become active after the menu exit. The native flag's two-second tail does not
delay the start. The ASL start action starts both timing methods in LiveSplit.
The introduction is timed with the default start;
**Start: After Cutscene** excludes it. Once the timer starts, subsequent loads pause
only Game Time.

A saved run loaded through Continue cannot satisfy the fresh-save check.
Unavailable save data disables Auto Start without preventing the timing state
from being read. Returning to the main menu, replacing the GameInstance or losing
readable timing state cancels the pending start. Attachment during a load does
not start retrospectively.

The ending requires an observed non-final cinematic state on the same pawn,
followed by `Cinematic_End` playback. Preparing the sequence does not split.
Attaching with the ending already selected cannot arm it. Menus, loading, unreadable
endpoint data and pawn replacement discard previous endpoint observations.
Successful opening is latched until the main menu or a new GameInstance.

The script performs bounded pointer reads at 60 Hz. It does not scan the process,
global object array, levels or actors during normal polling. Unreadable game or
loading-screen state discards transition history and cancels a pending start.
Unavailable ending data disarms the final signal without changing the loading
sample. Reader recovery cannot manufacture a start or ending transition.

When any card option is enabled, the reader resolves at most 256 inventory FNames
(8 bytes each), validates the array bounds and checks that its header stays stable
across the read, with one immediate retry for growth during pickup. No card
polling occurs when all card settings are disabled. Menus, loading, unreadable
inventory and component replacement invalidate the current card sample. The next
readable inventory seeds a new baseline, avoiding splits from restored data.
Inventory-only read failures never pause Game Time.

When the fingerprint option is enabled, the reader checks four 16-byte row handles
in `AC_Inventory.fingerprint`. Each contains a `DT_Fingerprint` pointer followed
by an 8-byte FName. The game preallocates all four slots with `None` rows, so array
length alone does not indicate completion. `SetFingerPrint` places each fragment
at its data-table `Position` (0 through 3). Completion requires `Jonny_0` through
`Jonny_3` in their matching slots: FName base `Jonny` with stored numbers 1 through
4. Mixed crew, duplicate or misplaced fragments cannot count as complete. The
reader validates the table, array bounds and header stability, with one immediate
retry. Fragment read failures invalidate the baseline without pausing Game Time.

## Validation

VisionElf confirmed the first-loading-screen start works in the supported build.
The optional **Start: After Cutscene** mode was added on 2026-10-06 and still
requires manual validation after both playing and skipping the introduction.

Earlier read-only observations on 2026-10-02 distinguished the initial New Game
load, introduction-skip load, gameplay readiness and final Pod opening. Later
observations identified the card, fingerprint, drawing and projection signals.
The memory-reader design above reflects those observations; it does not establish
support for other executable builds.

The standalone regression sources have been updated for loading-end starts and
the optional second-load start. They were not run during repository preparation.
The synthetic host is a development aid and does not replace validation in the
actual LiveSplit component. From the repository root, use:

```powershell
dotnet run --project tests/Rivage.Tests.csproj
```

This ASL script is loaded directly by LiveSplit and has no standalone build step.
Developed by Codex, under the supervision of VisionElf.
