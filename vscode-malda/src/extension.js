const fs = require("fs");
const path = require("path");
const vscode = require("vscode");
const {
  LanguageClient,
  TransportKind,
} = require("vscode-languageclient/node");

let client;

function readTypeStrict() {
  return vscode.workspace.getConfiguration("malda").get("types.strict", true);
}

function readCliPath() {
  return resolveConfiguredPath(vscode.workspace.getConfiguration("malda").get("cli.path") ?? "malda");
}

function resolveWorkspaceFolder() {
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

function resolveConfiguredPath(value) {
  if (!value || typeof value !== "string") {
    return value;
  }
  const folder = resolveWorkspaceFolder();
  if (folder) {
    return value.replace(/\$\{workspaceFolder\}/g, folder);
  }
  return value;
}

async function runCurrentMaldaFile() {
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

function activate(context) {
  context.subscriptions.push(
    vscode.commands.registerCommand("malda.runFile", () => {
      void runCurrentMaldaFile();
    })
  );

  context.subscriptions.push(
    vscode.debug.registerDebugAdapterDescriptorFactory("malda", {
      createDebugAdapterDescriptor() {
        return new vscode.DebugAdapterExecutable(readCliPath(), ["debug-adapter"]);
      },
    })
  );

  const config = vscode.workspace.getConfiguration("maldaLanguageServer");
  const serverPath = resolveConfiguredPath(config.get("path") ?? "malda-lsp");

  const serverOptions = {
    command: serverPath,
    args: [],
    transport: TransportKind.stdio,
  };

  const clientOptions = {
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

  client.start().catch((err) => {
    void vscode.window.showErrorMessage(
      `MALDA language server failed to start (${serverPath}). Set maldaLanguageServer.path to malda-lsp.exe. ${err}`
    );
  });

  vscode.workspace.onDidChangeConfiguration((e) => {
    if (!e.affectsConfiguration("malda.types.strict") || !client) {
      return;
    }
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

function deactivate() {
  if (client) {
    return client.stop();
  }
  return undefined;
}

module.exports = { activate, deactivate };
