# lazydotnet.UiTests — TUI snapshot regression

Renders Spectre.Console components to **deterministic text** and snapshots them with
[Verify](https://github.com/VerifyTests/Verify). This is the lazydotnet analogue of netclaw's
VHS screenshot regression — but on plain text instead of pixels, so it is stable across machines
and OSes and produces reviewable diffs (no ImageMagick, no font/antialias drift, no byte-for-byte
PNG fragility).

## How it works

- `TuiSnapshot.Render(renderable)` writes an `IRenderable` into a `TestConsole` with a **pinned
  width/height** (the only source of non-determinism) and returns the captured text.
- `TuiSnapshot.RenderWithColor(...)` additionally captures ANSI colour sequences. Use it only when
  colour is what's under test (e.g. an error panel must be red); the escape codes make diffs noisy.
- Baselines live in `Snapshots/*.verified.txt` and are committed. A mismatch fails the test and
  drops a `*.received.txt` next to the baseline for review.

## Adding a snapshot test

```csharp
[Fact]
public Task MyComponent_RendersExpectedLayout()
{
    var renderable = new MyComponent(...).GetRenderable(TuiSnapshot.DefaultWidth, TuiSnapshot.DefaultHeight);
    return Verify(TuiSnapshot.Render(renderable));
}
```

First run fails (no baseline) and writes `MyComponent_RendersExpectedLayout.received.txt`. Review it,
then promote:

```bash
mv Snapshots/<name>.received.txt Snapshots/<name>.verified.txt
```

Commit the `.verified.txt`. `*.received.*` is gitignored.

## Coverage targets

Most `src/UI` components expose an `IRenderable` entrypoint (`GetRenderable` / `GetContent`) — each
is a snapshot candidate. Seeded so far: modals (`ConfirmationModal`, `Modal`), `Notification`.
Still open: tabs (`TestDetailsTab`, `ExecutionTab`, `NuGetDetailsTab`, `ProjectReferencesTab`),
panes (`SolutionExplorer`, `ProjectDetailsPane`, `WorkspacePane`, `LogViewer`), the picker modals,
and full-screen `DashboardScreen` composition.
```
