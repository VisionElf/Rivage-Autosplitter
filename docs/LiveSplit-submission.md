# LiveSplit catalogue submission

Research date: 2026-10-06. Repository: https://github.com/VisionElf/Rivage-Autosplitter. Catalogue submission remains pending.

LiveSplit's official [Adding an Auto Splitter instructions](https://github.com/LiveSplit/LiveSplit.AutoSplitters#adding-an-auto-splitter)
ask authors to submit a change to
[`LiveSplit.AutoSplitters.xml`](https://github.com/LiveSplit/LiveSplit.AutoSplitters/blob/master/LiveSplit.AutoSplitters.xml).
Maintainers review and merge the pull request. Catalogue inclusion makes the
script available for automatic download and activation; it is not a guarantee
of compatibility with every game build or acceptance by a leaderboard.

No exact `<Game>Rivage</Game>` entry was found in the catalogue inspected on this date.
Check the current catalogue again before submitting to avoid duplicates.

## Publication steps

1. The publication target is `VisionElf/Rivage-Autosplitter`, under the MIT licence.
   Credits are Codex (development) and VisionElf (supervision).
2. Validate the current script in LiveSplit, including both start modes,
   introduction playback/skip, load removal and enabled split options. Update
   the regression results and record remaining limitations.
3. Keep `Rivage.asl` at the repository root; pushes to `main` trigger releases.
4. Let the push to `main` complete the automatic release, then verify that
   `releases/latest/download/Rivage.asl` downloads the script directly.
5. Fork `LiveSplit/LiveSplit.AutoSplitters` and add the `<AutoSplitter>` block
   inside its existing `<AutoSplitters>` root. Use the game name matching the
   LiveSplit splits entry; confirm its capitalization before submission.
6. Open a pull request describing Auto Start, load removal, optional splits,
   supported Steam build and validation. Address the maintainers' review.

The draft follows existing ASL entries: `<Games>`, `<URLs>`, `<Type>Script</Type>`,
`<Description>` and `<Website>`. ASL does not require conversion to WebAssembly.
Only `Rivage.asl` needs to be downloaded by LiveSplit; the test support is not
part of that distribution.
