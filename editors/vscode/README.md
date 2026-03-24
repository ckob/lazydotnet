# lazydotnet for VS Code

This is the official Visual Studio Code extension for [lazydotnet](https://github.com/ckob/lazydotnet), a terminal-based UI for managing .NET solutions and projects.

## Requirements

Before using this extension, you **must** install the following dependencies:

1. **[.NET 10 SDK or runtime](https://dotnet.microsoft.com/download/dotnet/10.0)** or later.
2. **lazydotnet** CLI tool.

To install `lazydotnet` globally, run the following command in your terminal:

```bash
dotnet tool install --global lazydotnet
```

*Note: The extension will prompt you to install or update `lazydotnet` if it's not found or outdated.*

## Features

This extension integrates the `lazydotnet` terminal UI directly into VS Code, allowing you to seamlessly manage your .NET projects without leaving the editor. 

When you launch `lazydotnet` via the extension, it enables IPC (Inter-Process Communication) so that when you choose to "edit" a file or view a test failure inside `lazydotnet`, the file opens directly in your active VS Code editor.

`lazydotnet` provides an interactive, keyboard-driven interface for common .NET development tasks:

*   **Solution Explorer:** Navigate your solution structure (`.sln`, `.slnx`, `.slnf`, and `.csproj` files) with vim-style keybindings.
*   **Project Management:** Build, run, and stop individual projects or the entire solution.
*   **Project References:** View, add, and remove project references with a visual picker dialog.
*   **NuGet Packages:** Manage NuGet packages with color-coded version indicators, update outdated packages, and add new ones.
*   **Tests:** Automatically discover and run tests (xUnit, NUnit, MSTest) with hierarchical tree views and live output.
*   **Execution Log:** View live log streaming from running projects.
*   **Workspace Management:** Switch between different solutions in the same directory.
*   **Editor Integration:** Open project files, test files, and navigate to test failures directly in VS Code.

## Usage

1. Open a .NET solution or project folder in VS Code.
2. Open the Command Palette (`Ctrl+Shift+P` on Windows/Linux, `Cmd+Shift+P` on macOS).
3. Search for and execute **"lazydotnet: Open"** (`lazydotnet.open`).
4. A new terminal panel will open running `lazydotnet`, automatically connected to your current workspace.

## Issues and Feedback

For bug reports, feature requests, or general feedback, please visit the main [lazydotnet GitHub repository](https://github.com/ckob/lazydotnet).