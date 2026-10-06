# Rivage Autosplitter

Auto Start, load removal and configurable splits for the supported Windows Steam
build of Rivage, written as an ASL script for LiveSplit.

## Installation

Download [Rivage.asl from the latest release](https://github.com/VisionElf/Rivage-Autosplitter/releases/latest/download/Rivage.asl).

In LiveSplit, add **Control → Scriptable Auto Splitter** in **Edit Layout**,
then select the script in its settings. Enable Start and Split, select Game Time
for load removal, and save the layout.

Configure settings before selecting New Game.

## Features

- **Start: After Cutscene**, the first setting, defaults off. Off starts RTA
  and Game Time after the first New Game loading screen. On waits until the
  second loading screen closes, after the introduction or its skip.
- Subsequent loading screens pause only Game Time.
- The final Pod opening sequence splits once.
- Independent optional splits cover eight cards, Jonny's complete fingerprint,
  the Lobby decoded map, the Laboratory drawing and the Horizon probe projection.
- Continue and mid-game attachment do not auto-start. Reset the timer manually.

Only the Steam executable documented in [the technical reference](docs/Technical-reference.md)
is supported. Unknown hashes are rejected; offsets require verification after
game updates. See that guide for settings and memory signals.

## Automatic releases

Pushing to `main` triggers [the release workflow](.github/workflows/release.yml).
It publishes `Rivage.asl` and its SHA-256 checksum in a release tagged with the
source commit. Re-running a workflow updates that commit's release. The release
for the current `main` commit is marked **Latest**. A manual run is also available
from the Actions tab on `main`.

The stable download address for the LiveSplit catalogue is:

```text
https://github.com/VisionElf/Rivage-Autosplitter/releases/latest/download/Rivage.asl
```

The workflow uses
GitHub's built-in token with `contents: write`; no personal access token is needed.
It packages the script directly without compiling it or running game tests.
Workflow execution remains unverified until the first GitHub run.

See [LiveSplit catalogue submission](docs/LiveSplit-submission.md) and the
[draft XML entry](docs/LiveSplit-entry.xml) for the submission procedure.

## Development

`Rivage.asl` is the distributable source; users do not need a compiler or DLL.
`tests/` contains the Rivage regression suites. `support/AslHost/` contains the
minimal ASL test host for those suites; LiveSplit does not
download or use this support library.

To run the suites on Windows with the .NET 10 SDK:

```powershell
dotnet run --project tests/Rivage.Tests.csproj
```

The tests read synthetic memory owned by their own process. They do not call
the host's game-attachment polling method and do not prove compatibility with
the actual LiveSplit component. No tests were run during repository preparation.
The first-loading-screen start was confirmed working by VisionElf; the
after-cutscene option still requires manual verification.

## Credits and licence

Developed by **Codex**, under the supervision of **VisionElf**.
VisionElf directed the features and performed the reported manual validation.

Released under the [MIT licence](LICENSE).
This project is not affiliated with the developers of Rivage or LiveSplit.
