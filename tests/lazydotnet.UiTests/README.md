# lazydotnet.UiTests — TUI Snapshot Regression

Renders Spectre.Console TUI components to **deterministic text** and snapshots them with [Verify](https://github.com/VerifyTests/Verify) (`Verify.XunitV3`).

Unlike pixel-based terminal screenshots, text-based snapshot testing captures the exact ANSI/Unicode text layout rendered to a fixed `TestConsole`. This produces cross-platform stability (Linux, macOS, Windows) and reviewable git diffs without font, anti-aliasing, or rendering engine drift.

---

## How It Works

- **`TuiSnapshot.Render(renderable)`**: Renders an `IRenderable` into a `TestConsole` pinned to fixed dimensions (`100` width × `30` height) and returns plain text. Line endings are normalized to LF (`\n`) and backslashes are normalized to `/` for OS independence.
- **`TuiSnapshot.RenderWithColor(renderable)`**: Captures raw ANSI color escape sequences. Use this specifically when styling or color layout is under test (e.g., error notification panels).
- **Snapshot Baselines**: Approved baselines are committed under `Snapshots/*.verified.txt`. On test failure, Verify writes `*.received.txt` alongside the baseline for diffing.

---

## Writing & Promoting Snapshot Tests

### Adding a Test

```csharp
[Fact]
public Task MyComponent_RendersExpectedLayout()
{
    var renderable = new MyComponent(...).GetRenderable(TuiSnapshot.DefaultWidth, TuiSnapshot.DefaultHeight);
    return Verify(TuiSnapshot.Render(renderable));
}
```

### Reviewing & Promoting Baselines

When a test runs for the first time or when a UI change is intended:

1. **Review Diff:** Compare `Snapshots/<Test>.received.txt` against `Snapshots/<Test>.verified.txt`.
2. **Accept Single Baseline:**
   ```bash
   mv tests/lazydotnet.UiTests/Snapshots/MyTest.received.txt tests/lazydotnet.UiTests/Snapshots/MyTest.verified.txt
   ```
3. **Accept All Baselines (`Verify.Cli`):**
   ```bash
   dotnet tool install -g Verify.Cli
   verify accept
   ```

---

## Test Boundaries & Scope

### What Belongs in `lazydotnet.UiTests`
* Pure UI component layout, borders, headers, key hints, and text wrapping.
* Empty tab placeholder states (`ExecutionTab`, `NuGetDetailsTab`, `TestDetailsTab`).
* Modals (`ConfirmationModal`, `Modal`, `ProjectPickerModal`, `SelectionModal<T>`, `TestDetailsModal`).
* Static notifications and log viewer formatting.

### What Belongs in `lazydotnet.IntegrationTests`
* Populated tab/pane states that depend on live service I/O (MSBuild, NuGet API, or Test Platform output).
* Dynamic state changes driven by real workspace loading or background execution.

---

## Rules for Deterministic Snapshots

1. **Terminal Size:** Always pin terminal dimensions using `TuiSnapshot.DefaultWidth` (`100`) and `TuiSnapshot.DefaultHeight` (`30`).
2. **Relative File Paths:** Feed relative file paths into fixtures (e.g., `tests/SampleApp.Tests/CalculatorTests.cs`) so path resolution is machine-independent.
3. **Avoid Spinners & Time-Based Views:** Do not snapshot components displaying active `SpinnerHelper` spinners or `DateTime.UtcNow` stamps, as frame timing varies per run.
4. **Static State Cleanup:** Ensure tests touching static UI state (e.g., `Notification`) restore state in a `try / finally` block to prevent parallel test contamination.
