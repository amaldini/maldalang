import * as fs from "fs";
import * as path from "path";
import * as vscode from "vscode";
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
  TransportKind,
} from "vscode-languageclient/node";

let client: LanguageClient | undefined;

function readTypeStrict(): boolean {
  return vscode.workspace.getConfiguration("malda").get<boolean>("types.strict", true);
}

function readCliPath(): string {
  return resolveConfiguredPath(vscode.workspace.getConfiguration("malda").get<string>("cli.path") ?? "malda");
}

function resolveWorkspaceFolder(): string | undefined {
  const fromWorkspace = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
  if (fromWorkspace) {
    return fromWorkspace;
  }

  const filePath = vscode.window.activeTextEditor?.document.uri.fsPath;
  if (!filePath) {
    return undefined;
  }

  let current = path.dirname(filePath);
  while (current) {
    if (fs.existsSync(path.join(current, "MaldaLang.sln"))) {
      return current;
    }
    const parent = path.dirname(current);
    if (parent === current) {
      break;
    }
    current = parent;
  }

  return path.dirname(filePath);
}

function resolveConfiguredPath(value: string): string {
  const folder = resolveWorkspaceFolder();
  if (folder) {
    return value.replace(/\$\{workspaceFolder\}/g, folder);
  }
  return value;
}

class MaldaDebugAdapterFactory implements vscode.DebugAdapterDescriptorFactory {
  createDebugAdapterDescriptor(): vscode.ProviderResult<vscode.DebugAdapterDescriptor> {
    return new vscode.DebugAdapterExecutable(readCliPath(), ["debug-adapter"]);
  }
}

async function runCurrentMaldaFile(): Promise<void> {
  const editor = vscode.window.activeTextEditor;
  if (!editor || editor.document.languageId !== "malda") {
    void vscode.window.showErrorMessage("Open a .malda file to run.");
    return;
  }
  if (editor.document.isUntitled) {
    void vscode.window.showErrorMessage("Save the .malda file before running.");
    return;
  }
  if (editor.document.isDirty) {
    await editor.document.save();
  }

  const cli = readCliPath();
  if (!cli) {
    void vscode.window.showErrorMessage("Set malda.cli.path to a built malda executable.");
    return;
  }
  if (path.isAbsolute(cli) && !fs.existsSync(cli)) {
    void vscode.window.showErrorMessage(
      `MALDA CLI not found: ${cli}. Build with: dotnet build MaldaLang -o artifacts/malda-cli`
    );
    return;
  }

  const file = editor.document.uri.fsPath;
  const cwd = path.dirname(file);
  const task = new vscode.Task(
    { type: "malda", task: "run" },
    vscode.workspace.workspaceFolders?.[0]
      ? vscode.TaskScope.Workspace
      : vscode.TaskScope.Global,
    "Run MALDA file",
    "malda",
    new vscode.ProcessExecution(cli, [file], { cwd })
  );
  task.presentationOptions = {
    reveal: vscode.TaskRevealKind.Always,
    panel: vscode.TaskPanelKind.Shared,
    focus: true,
    clear: true,
  };
  task.problemMatchers = [];
  await vscode.tasks.executeTask(task);
}

export function activate(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.commands.registerCommand("malda.runFile", () => {
      void runCurrentMaldaFile();
    })
  );

  context.subscriptions.push(
    vscode.debug.registerDebugAdapterDescriptorFactory(
      "malda",
      new MaldaDebugAdapterFactory()
    )
  );

  const config = vscode.workspace.getConfiguration("maldaLanguageServer");
  const serverPath = resolveConfiguredPath(config.get<string>("path") ?? "malda-lsp");

  const serverOptions: ServerOptions = {
    command: serverPath,
    args: [],
    transport: TransportKind.stdio,
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: "file", language: "malda" }],
    synchronize: {
      fileEvents: vscode.workspace.createFileSystemWatcher("**/*.malda"),
      configurationSection: ["malda", "malda.types"],
    },
    initializationOptions: {
      typeStrict: readTypeStrict(),
    },
  };

  client = new LanguageClient(
    "maldaLanguageServer",
    "MALDA Language Server",
    serverOptions,
    {
      ...clientOptions,
      outputChannelName: "MALDA",
    }
  );

  void client.start().catch((err) => {
    void vscode.window.showErrorMessage(
      `MALDA language server failed to start (${serverPath}). Set maldaLanguageServer.path to malda-lsp.exe. ${err}`
    );
  });

  vscode.workspace.onDidChangeConfiguration((e) => {
    if (!e.affectsConfiguration("malda.types.strict") || !client) {
      return;
    }
    // Push updated init-style option via didChangeConfiguration payload the server understands.
    void client.sendNotification("workspace/didChangeConfiguration", {
      settings: {
        malda: {
          types: {
            strict: readTypeStrict(),
          },
        },
      },
    });
  });
}

export function deactivate(): Thenable<void> | undefined {
  if (client) {
    return client.stop();
  }
  return undefined;
}
