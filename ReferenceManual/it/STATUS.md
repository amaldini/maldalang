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
| index.html | 5e45be843f41036647b8413eb7e16ac5534ee51d46ec296cd8167c4f0e47371e |
| 01-introduction.html | 5303401633f687b53083ef671bf53fca68e5cecc94b71a00133dff9a86f6c310 |
| 02-tools.html | 3639b8d6a126fd5dd10746faa92a0a454e7eb94b234a51bd0a4a56c76e03ca75 |
| 03-lexical-structure.html | e7075a436d1fbbaebadafd270f3c66ddd14e08a76993deb0af2053b2daa22754 |
| 04-data-types.html | dae3a2688e6ebff71f2f0e4cac63d67a793e2d52ccff3a69a0c76483f13d9828 |
| 05-variables.html | 10497d78b519fc06c0f1bdde4043c05aba8ef3041ae866d92eebdf30fdf8aba0 |
| 06-arrays.html | a707d5ca77923de9479b378ed8a37151fa9a1ede82118a76147a4cbfdb9506d5 |
| 07-expressions.html | 03f095bbb89c9c70b1ec94017a5f3d17561371c8e5846adf03b7a3511eb787e4 |
| 08-control-structures.html | e565b52ff2e24939a8857c6e78b9ac197bfe4effc2d0f28d27a6bb1a2a0af589 |
| 09-functions.html | 9b4cbf6676cb3131887bf7fcb4dd13f2f670811c0e7e8779f69a06bf58fb613e |
| 10-prompts.html | 9b308506e8be143647cff9e5e4d58dbb31c75308b67ce7a20e2a3dfc388e253f |
| 11-classes-objects.html | bfc4e4c08a10efc730565642f0efa18e99e8c31a0c6982252eae706a2e946602 |
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
