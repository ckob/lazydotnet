import * as vscode from "vscode";
import { exec } from "node:child_process";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";

const MIN_VERSION = "0.5.3";
const isWindows = os.platform() === "win32";

let activeTerminal: vscode.Terminal | undefined;
let ipcState: { ipcPath: string; watcher: fs.FSWatcher } | undefined;
let installedCheckPromise: Promise<boolean> | undefined;
let versionCheckPromise: Promise<string> | undefined;

function checkInstalledAsync(): Promise<boolean>
{
    return new Promise((resolve) =>
    {
        const command = isWindows ? "where lazydotnet" : "which lazydotnet";
        exec(command, (error, stdout) =>
        {
            resolve(!error && stdout.trim().length > 0);
        });
    });
}

function normalizeVersion(version: string): string
{
    return version.split("-")[0];
}

function getVersionAsync(): Promise<string>
{
    return new Promise((resolve) =>
    {
        exec("lazydotnet --version", (error, stdout) =>
        {
            resolve(error ? "0.0.0" : normalizeVersion(stdout.trim()));
        });
    });
}

function compareVersions(a: string, b: string): number
{
    const pa = a.split(".").map(Number);
    const pb = b.split(".").map(Number);
    for (let i = 0; i < Math.max(pa.length, pb.length); i++)
    {
        const diff = (pa[i] || 0) - (pb[i] || 0);
        if (diff !== 0) return diff;
    }
    return 0;
}

function setupIpc(): string
{
    const ipcPath = path.join(os.tmpdir(), `lazydotnet-vscode-ipc-${process.pid}.tmp`);
    fs.writeFileSync(ipcPath, "");

    const watcher = fs.watch(ipcPath, () =>
    {
        const content = fs.readFileSync(ipcPath, "utf-8").trim();
        if (content)
        {
            handleIpcMessage(content);
        }
    });

    ipcState = { ipcPath, watcher };
    return ipcPath;
}

function cleanupIpc(): void
{
    if (!ipcState) return;
    ipcState.watcher.close();
    try { fs.unlinkSync(ipcState.ipcPath); } catch { }
    ipcState = undefined;
}

async function handleIpcMessage(line: string): Promise<void>
{
    const parts = line.split("\t");
    const filePath = parts[0]?.trim();
    const lineNum = parts.length > 1 ? Number.parseInt(parts[1], 10) : 0;

    if (!filePath) return;

    const uri = vscode.Uri.file(filePath);
    const doc = await vscode.workspace.openTextDocument(uri);
    const position = new vscode.Position(Math.max(0, lineNum > 0 ? lineNum - 1 : 0), 0);
    await vscode.window.showTextDocument(doc, {
        preview: false,
        selection: new vscode.Range(position, position),
    });
}

async function openAsync(): Promise<void>
{
    installedCheckPromise = installedCheckPromise ?? checkInstalledAsync();
    const installed = await installedCheckPromise;

    if (!installed)
    {
        const selection = await vscode.window.showErrorMessage(
            "lazydotnet is not installed globally. You need to install it via the .NET CLI first.",
            "Install lazydotnet"
        );

        if (selection === "Install lazydotnet")
        {
            const installTerminal = vscode.window.createTerminal("lazydotnet Install");
            installTerminal.show();
            installTerminal.sendText("dotnet tool install -g lazydotnet");
        }

        installedCheckPromise = undefined;
        versionCheckPromise = undefined;

        return;
    }

    versionCheckPromise = versionCheckPromise ?? getVersionAsync();
    const version = await versionCheckPromise;
    if (compareVersions(version, MIN_VERSION) < 0)
    {
        const selection = await vscode.window.showErrorMessage(
            `lazydotnet requires at least version ${MIN_VERSION}.`,
            "Update lazydotnet"
        );

        if (selection === "Update lazydotnet")
        {
            const installTerminal = vscode.window.createTerminal("lazydotnet Install");
            installTerminal.show();
            installTerminal.sendText("dotnet tool update -g lazydotnet");
        }

        installedCheckPromise = undefined;
        versionCheckPromise = undefined;
        return;
    }

    if (activeTerminal)
    {
        activeTerminal.show();
        vscode.commands.executeCommand("workbench.action.focusActiveEditorGroup");
        return;
    }

    cleanupIpc();
    const ipcPath = setupIpc();

    const workspaceFolder = vscode.workspace.workspaceFolders?.[0]?.uri;
    const cwd = workspaceFolder?.fsPath ?? os.homedir();

    activeTerminal = vscode.window.createTerminal({
        name: "lazydotnet",
        shellPath: isWindows ? "cmd.exe" : "sh",
        shellArgs: isWindows ? ["/K"] : [],
        location: vscode.TerminalLocation.Editor,
        cwd,
        env: { LAZYDOTNET_VSCODE_IPC_FILE: ipcPath },
    });

    activeTerminal.show();
    activeTerminal.sendText("lazydotnet && exit");

    vscode.commands.executeCommand("workbench.action.focusActiveEditorGroup");
}

export function activate(context: vscode.ExtensionContext): void
{
    installedCheckPromise = checkInstalledAsync();
    versionCheckPromise = getVersionAsync();

    const disposable = vscode.commands.registerCommand("lazydotnet.open", () =>
    {
        openAsync();
    });

    context.subscriptions.push(
        disposable,
        vscode.window.onDidCloseTerminal((terminal) =>
        {
            if (terminal === activeTerminal)
            {
                activeTerminal = undefined;
                cleanupIpc();
                setTimeout(() =>
                {
                    vscode.commands.executeCommand("workbench.action.focusActiveEditorGroup");
                }, 50);
            }
        })
    );
}

export function deactivate(): void
{
    cleanupIpc();
}
