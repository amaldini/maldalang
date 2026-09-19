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
| index.html | 46fc716757e678a816406ec017c0d54777186e38b97988b4d5a2b6fe1c5da466 |
| learn.html | 8f7591aae9e47ae50a5e8c0f30295263111905bd6d8f9546fbb04f8066bb74d4 |
| 01-introduction.html | b039af664bfa87db34b4612bac1d794e041184108ab903bbf2805a22e4a0f227 |
| 02-tools.html | 3639b8d6a126fd5dd10746faa92a0a454e7eb94b234a51bd0a4a56c76e03ca75 |
| 03-lexical-structure.html | 3da2779adff2ae76307c1404a3b4b6e0bb18c7c1d25f9b3633034443552a6882 |
| 04-data-types.html | ead2a9263370d7eee4d99e36666bad701e5e0304529b4256a985a6e1c7e75f1a |
| 05-variables.html | 10497d78b519fc06c0f1bdde4043c05aba8ef3041ae866d92eebdf30fdf8aba0 |
| 06-arrays.html | 762b978d87995b0bb22f14cc24b042ceaf40323dd5f834320502ca82ea25754a |
| 07-expressions.html | 21cce6928f34d7e65533a57c547ba3d22e4989b2910c43939d0e43831be4f8c7 |
| 08-control-structures.html | e565b52ff2e24939a8857c6e78b9ac197bfe4effc2d0f28d27a6bb1a2a0af589 |
| 09-functions.html | 871c09b648987e481ee7ee0684df18955a086a2119f20c133c44c03f0917ef30 |
| 10-prompts.html | 9b308506e8be143647cff9e5e4d58dbb31c75308b67ce7a20e2a3dfc388e253f |
| 11-classes-objects.html | f93e6297e76898a367b833c8dc6fe71d36e73c9f5f5e46031bd3e8e7d5163d45 |
| 12-input-output.html | 76af4bec969d742bbfedc1a5a4d48a7231e33008cca53bd7357198373d2a4b9e |
| 13-built-in-functions.html | e0d76ab9e39165be9a778e5fece5e85a0434c3d8b47006f13cc6b82af690d6de |
| 14-graphs.html | 6d0056a2a92c132fb1bbd69bf128b10150877568ccf9b5e7cfd4810bf82b4a53 |
| 15-vectordb.html | 25402e740dca3177186c0089dc06caa77945729e94c8bc0c65fad3683bf99e16 |
| 16-database.html | 1135d01b02ff20c2dba32d72f576928cbf6d1b212c64799e2d58b4bf8698b7e6 |
| 17-actors.html | 22b280db3a4b496a7e29f7bc7aec091b5a58c7be50d784e1acc9f2c4f289e568 |
| 18-agent-orchestration.html | e620cbcb0378f9cdb7b3d6a78046ece517def3cff757c742c47fbfd6dc943e25 |
| 19-graph-memory.html | 2782af3364b7d01edfb1f31bbe5f6aa4b63d799886c5ffcbbd405e537cec22dd |
| 20-mcp-server.html | 6811669af074756655e0232f75d93cecd3bc555ee2d15f3ee53cc118d8b2e2bd |
| 21-acp.html | 05bf83e84d7854c312bfaa8495c17dfc70c7d5e1e39fdaeb5e6ac15f4bd53e29 |
| 22-durable-workflows.html | 9eedd68c07d1f58fd2a8d0abc8b2d61e4b60b09b2eef91e4dbcc680558dffa7f |
| 23-agentic-runs.html | e4a638786fd6d70e83d17a604a24aa27d96cc76bfb946be7b33489e4f600f0e8 |
| 24-web-ui-hub.html | 2c0d3a23b221da510cd4097f222e846b52c23382f5698e3df50a2dee0a3388aa |
| 25-web-ui.html | f895eb5df3f091af5ad9926e032940e708641b018fac356953c20d40c8c43791 |
| 26-http-server-html-ui.html | c5b32243ea9eaeb54fe662be014e6f04454522f0ee246a8ef43f708a0a438a87 |
| 27-browser-javascript-backend.html | 400c03571e6df29e6aa7b2a03642333c33e358a988b54d8b8a766f7d04a14fd9 |
| 28-rest-api.html | d44e3225e113ef50bee60293780211ccbded539e7db87611302f830a0d878cac |
| 29-rest-web-client.html | 91c4f2974480b1e6c9408dd5a94bbdb2f94250b2197d3e03b8c64ecfb25eb624 |
| 30-full-stack-development.html | ef406f4b47c393aaa7a15782213ca4e11c62d9693567a1a31343bd4da0f6c6e4 |
| 31-dotnet-interop.html | 06aed0859ea49b90a656c1c427ec64062e00e51aa63cc3070264ece3d0247ba8 |
| 32-device-integration.html | 44abf76f864803c3a4421834ccf4f9eb9e8a0143ac1527d44cd3e9370bd89b3d |
| 33-personal-assistant.html | 4a13b0f9a2f90ddbf5b42a8ca003174ff55d0cb1bf43b8e310e0e835c77d0323 |
| 34-examples.html | d9411075720775603ed6253e84c60b147c6b0e48425fd55cea04ea43803c59b6 |
| 35-property-testing.html | 8010e4cb701eb6841c5b8b159088f81f56454bc9eedde2323299a25e8a6d1381 |
| 36-grammar.html | bc8efe0893d5c73cdfa3f3a87ee6ef6aca7b531de0df189272472d24a7a94b22 |
| 37-appendix.html | b533631300e789165d340361096bbdf96ac27df784b5778e91cc845c32d31f9f |
| 38-appendix-gpu-billiards.html | 40d3d5376471d827c9705df2c3112b8119191e8f4f5f7ce152416158e4123f99 |
