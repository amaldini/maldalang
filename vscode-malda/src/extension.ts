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
  return vscode.workspace.getConfiguration("malda").get<string>("cli.path") ?? "malda";
}

class MaldaDebugAdapterFactory implements vscode.DebugAdapterDescriptorFactory {
  createDebugAdapterDescriptor(): vscode.ProviderResult<vscode.DebugAdapterDescriptor> {
    return new vscode.DebugAdapterExecutable(readCliPath(), ["debug-adapter"]);
  }
}

export function activate(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.debug.registerDebugAdapterDescriptorFactory(
      "malda",
      new MaldaDebugAdapterFactory()
    )
  );

  const config = vscode.workspace.getConfiguration("maldaLanguageServer");
  const serverPath = config.get<string>("path") ?? "malda-lsp";

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

  client.start();

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
