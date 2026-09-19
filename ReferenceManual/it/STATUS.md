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
| index.html | ff351d680c58e9fcca58b69e7ba0953a1bc70047705adabb81d2948aef7b3979 |
| 01-introduction.html | 81f6ab14e42171407bca899d0b7cf8a3992f9eb7148d7c4c49734b0c8b3ea1b5 |
| 02-tools.html | eb9a7c62553dab896962286ba3764d7d891b77ef9a930ec25d3a991d96e4ad1e |
| 03-lexical-structure.html | e7075a436d1fbbaebadafd270f3c66ddd14e08a76993deb0af2053b2daa22754 |
| 04-data-types.html | dae3a2688e6ebff71f2f0e4cac63d67a793e2d52ccff3a69a0c76483f13d9828 |
| 05-variables.html | 45411eb63f6035e2cac4943d9a329bdcd5c52a71f83bb523d0ba7a94f6c3af06 |
| 06-arrays.html | a707d5ca77923de9479b378ed8a37151fa9a1ede82118a76147a4cbfdb9506d5 |
| 07-expressions.html | 03f095bbb89c9c70b1ec94017a5f3d17561371c8e5846adf03b7a3511eb787e4 |
| 08-control-structures.html | 14f9e539ffb87791b1c48007a50935f906e043248305429db1d05d26556981e8 |
| 09-functions.html | ce47443ab92805864f51cff331cafd773428b2acfefd8204a9593e639f7f1915 |
| 10-prompts.html | 9b308506e8be143647cff9e5e4d58dbb31c75308b67ce7a20e2a3dfc388e253f |
| 11-classes-objects.html | bfc4e4c08a10efc730565642f0efa18e99e8c31a0c6982252eae706a2e946602 |
| 12-input-output.html | 94650091609ea096f14cf10e09e4d5ec808d058384d17d6de2f7974b8bd38eda |
| 13-built-in-functions.html | e0d76ab9e39165be9a778e5fece5e85a0434c3d8b47006f13cc6b82af690d6de |
| 14-graphs.html | 6d0056a2a92c132fb1bbd69bf128b10150877568ccf9b5e7cfd4810bf82b4a53 |
| 15-vectordb.html | 25402e740dca3177186c0089dc06caa77945729e94c8bc0c65fad3683bf99e16 |
| 16-database.html | 1135d01b02ff20c2dba32d72f576928cbf6d1b212c64799e2d58b4bf8698b7e6 |
| 17-actors.html | 22b280db3a4b496a7e29f7bc7aec091b5a58c7be50d784e1acc9f2c4f289e568 |
| 18-agent-orchestration.html | 332f03510c4593493fc1b58c785d3c1a0978112a68b233688de684e6b6e77be1 |
| 19-graph-memory.html | 2782af3364b7d01edfb1f31bbe5f6aa4b63d799886c5ffcbbd405e537cec22dd |
| 20-mcp-server.html | 6811669af074756655e0232f75d93cecd3bc555ee2d15f3ee53cc118d8b2e2bd |
| 21-acp.html | 05bf83e84d7854c312bfaa8495c17dfc70c7d5e1e39fdaeb5e6ac15f4bd53e29 |
| 22-durable-workflows.html | 9eedd68c07d1f58fd2a8d0abc8b2d61e4b60b09b2eef91e4dbcc680558dffa7f |
| 23-agentic-runs.html | e4a638786fd6d70e83d17a604a24aa27d96cc76bfb946be7b33489e4f600f0e8 |
| 24-web-ui-hub.html | 5ef36fbdc02af952dcc298371f340ff825708aebf89e0099cb5266b48838794f |
| 25-web-ui.html | f895eb5df3f091af5ad9926e032940e708641b018fac356953c20d40c8c43791 |
| 26-http-server-html-ui.html | c5b32243ea9eaeb54fe662be014e6f04454522f0ee246a8ef43f708a0a438a87 |
| 27-browser-javascript-backend.html | a831a0599836679f74e1ffdef8dc0ef2b4e572002d44ac17303d07f0f2f2f3a9 |
| 28-rest-api.html | d44e3225e113ef50bee60293780211ccbded539e7db87611302f830a0d878cac |
| 29-rest-web-client.html | 91c4f2974480b1e6c9408dd5a94bbdb2f94250b2197d3e03b8c64ecfb25eb624 |
| 30-full-stack-development.html | ef406f4b47c393aaa7a15782213ca4e11c62d9693567a1a31343bd4da0f6c6e4 |
| 31-dotnet-interop.html | 06aed0859ea49b90a656c1c427ec64062e00e51aa63cc3070264ece3d0247ba8 |
| 32-device-integration.html | 44abf76f864803c3a4421834ccf4f9eb9e8a0143ac1527d44cd3e9370bd89b3d |
| 33-personal-assistant.html | 4a13b0f9a2f90ddbf5b42a8ca003174ff55d0cb1bf43b8e310e0e835c77d0323 |
| 34-examples.html | d0a4d41ba5ee682ddf95e2a1b875c5d581d5a829f3bc9b0667874fe775bb2dee |
| 35-property-testing.html | 8010e4cb701eb6841c5b8b159088f81f56454bc9eedde2323299a25e8a6d1381 |
| 36-grammar.html | bc8efe0893d5c73cdfa3f3a87ee6ef6aca7b531de0df189272472d24a7a94b22 |
| 37-appendix.html | b533631300e789165d340361096bbdf96ac27df784b5778e91cc845c32d31f9f |
| 38-appendix-gpu-billiards.html | a81124169ff2f18d9bc1467a4bb8e4ea6041866d40b8642f4ebbe7f59c17ee3e |
