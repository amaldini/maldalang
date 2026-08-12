import { workspace } from "vscode";
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
  TransportKind,
} from "vscode-languageclient/node";

let client: LanguageClient | undefined;

function readTypeStrict(): boolean {
  return workspace.getConfiguration("malda").get<boolean>("types.strict", true);
}

export function activate(): void {
  const config = workspace.getConfiguration("maldaLanguageServer");
  const serverPath = config.get<string>("path") ?? "malda-lsp";

  const serverOptions: ServerOptions = {
    command: serverPath,
    args: [],
    transport: TransportKind.stdio,
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: "file", language: "malda" }],
    synchronize: {
      fileEvents: workspace.createFileSystemWatcher("**/*.malda"),
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

  workspace.onDidChangeConfiguration((e) => {
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
