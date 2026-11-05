# Italian translation status

English in `ReferenceManual/` is canonical. Each row is the SHA-256 of
the LF-normalized English HTML this Italian page was translated from
(CRLF checkouts must hash the same). After changing an English chapter,
update `it/{file}` and regenerate this table:

```bash
python3 scripts/sync-reference-manual-it-status.py
```

| File | EN SHA-256 |
|------|------------|
| index.html | 1e8ba4510b965176131f143694ceb10e94aeb601c0764f77ac0799bf43734241 |
| learn.html | 28b639eb8e5758ad50dd5419d125a90fdfaca688e02ec65a2d9864d0a81b0617 |
| 01-introduction.html | 3faae900b280a6d65154205082f4a3e7b0af0b337a1a3d3b62657ce02e018bbc |
| 02-tools.html | 64d3b2910fa9d5d9c08a9664438158c8758cf27ad5bf92bd8747484f9c118b16 |
| 03-lexical-structure.html | 366b28a9907eaa994378d0c5a7e4e5704b76d913cc062e0bf41737f86a343204 |
| 04-data-types.html | 61f9970e65bc3e50afc8ed2a0cc8bd22494db9e0c4643e12cde87dccaebf6824 |
| 05-variables.html | 03ddd9d9d9802ef228be8dcc9697bff3059382163ecaa86987743dae23b89ba2 |
| 06-arrays.html | 2bf391db4c06ef6ebe497c87c0486d41586c6838b28fc4b502db455c898f7822 |
| 07-expressions.html | a932a63cbb24cabc33ec95616c9c8bd9f17c5e34194477e0ae7c8a10ebcf1b1c |
| 08-control-structures.html | 41120eb638febcba2fe818e3f5584af0fb297acea79ea29837db6c0379052c0f |
| 09-functions.html | 3a072e9c8cbdc373f52b0bc88a0ec40bf5796b4a2813691a74b0b237af56ab95 |
| 10-prompts.html | fd4e52c8069b992f5cb8f8c06b0422f32553560fd58f43d5bc06b7e78474f6b2 |
| 11-classes-objects.html | 685770f49cbc57bd1489908784357ed4a56d1f509c8774526e7530b0867ef2b7 |
| 12-input-output.html | f21dd962e0724a5e3fde2591afae0438ea33550b92ed823ea6a8a1941b0c75f3 |
| 13-built-in-functions.html | 2e882b612e21152323c1dbe0a499c35ccacf33a9cede2f5d6e392bfb6060418e |
| 14-neural-nets.html | 84ae6a8074d8718527de46cf7bf448c01e9b7ef128c4cfff1966dfd170d2f821 |
| 15-graphs.html | 4d8e12d5f1c2c26d58cb19a26f0c4a5907537a82d715d294e8808afaa4cd1be5 |
| 16-vectordb.html | 5c6a339e0a6e0243c17481efc35ab0ea870f04463b013ebaa0f6c5be7dbb8d41 |
| 17-database.html | f898dc435b95a0ffea67bdac2d5646b633854d599b91e91452647af1174e969a |
| 18-actors.html | 28fa3f246e9b159c8a6cc0e4a7686b8bbd5a9e37c76b716ff9dfb70dbf6973e7 |
| 19-agent-orchestration.html | 21fc28a11b5c02ad96bc73fddea841be6af6c28b0651a33cc234db33fde4efdc |
| 20-graph-memory.html | 994118d61b2536632d8cc03de4f951e3e1ebda0e0b23118c299e62e982d7c7f6 |
| 21-mcp-server.html | eccd18dacc6531e98ff57799a637fe7b4356a0219206f17da7c9e45d9a19ba5a |
| 22-acp.html | ecb6e70de7937e9f0105ec359dc9e4fb2f9834ca1b0bd82be9334cefdded313d |
| 23-durable-workflows.html | b21d08b75c8722ed00a87d074ea54c2c10e6ae1ede820e9c9ebe4afe31910518 |
| 24-agentic-runs.html | b7ce7fbdc4a6a3428b1af6355a715310fc4463bd6de4bb303b28036cb6e23794 |
| 25-web-ui-hub.html | 2caef0f3181b3cb5115d3a519bf81cf9576ad589b2a5cb3d0568ce26da4739e0 |
| 26-web-ui.html | d1b57bd7b420795a071f4cb71171b9d3e3cb678aaa606abb33384eca2a3aabf2 |
| 27-http-server-html-ui.html | e387b8368fe11992a4e4e760e6841cc55cb8b25d930d497c96a8ed9d222ed276 |
| 28-browser-javascript-backend.html | c4260e6197c3625c98f1687242d419c1a0b353ce1b5c7c506117780b10c1738c |
| 29-rest-api.html | 5e20eb10617efaf798ca90f7fb99ec8d76b1105fd1d8cd15b6cb205ac76782c5 |
| 30-rest-web-client.html | 27a49537b151b303b40d922786c615637255898b59bcc7aa1d69fa222b3bbc34 |
| 31-full-stack-development.html | c5ec3f8cf0ab9fa934076a67caa57e7b07759098d6ad69612778053ab48351e2 |
| 32-dotnet-interop.html | 02654ceb8aea37bbb3211488def27c6962315c07e21557571f358701faa2ee69 |
| 33-device-integration.html | 93d02da26d11fcffdff2cc7dbb3647b1d786acf1387860f1b5d61750e70764c5 |
| 34-personal-assistant.html | 9406cc80bf2f480ec203b6f2a3328953f4358972cb4e735ae6eb1124111b5851 |
| 35-examples.html | 0b340df91bfeddee47e5f7320f73d7cc4c4001b1697250626dbac4b7ab6a5218 |
| 36-property-testing.html | aa4efd46f60914a3b7f636e2478c8e9a0d25c876a73f936326f94b78912111e6 |
| 37-grammar.html | 95aa74bd74625ef1a1ed720af9cc07fda9e9e0d58bd5a7922d27f98cd9846c2b |
| 38-appendix.html | b11d5db9454f5c43d7a4822e4efde1b5dc869590d3064af5080335468d155ce8 |
| 39-appendix-gpu-billiards.html | b6f269c226323e5d9bc6fd80ba36b57537d3395327053bc9b5807dc19d30527e |
