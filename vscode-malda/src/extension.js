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

function resolveConfiguredPath(value) {
  if (!value || typeof value !== "string") {
    return value;
  }
  const folder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
  if (folder) {
    return value.replace(/\$\{workspaceFolder\}/g, folder);
  }
  return value;
}

function activate(context) {
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
