const vscode = require("vscode");
const {
  LanguageClient,
  TransportKind,
} = require("vscode-languageclient/node");

let client;

function readTypeStrict() {
  return vscode.workspace.getConfiguration("malda").get("types.strict", true);
}

function activate() {
  const config = vscode.workspace.getConfiguration("maldaLanguageServer");
  const serverPath = config.get("path") ?? "malda-lsp";

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

  client.start();

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
