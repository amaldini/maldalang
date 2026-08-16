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

function resolveConfiguredPath(value: string): string {
  const folder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
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

export function activate(context: vscode.ExtensionContext): void {
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
