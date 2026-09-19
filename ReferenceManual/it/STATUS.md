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
| index.html | 3c6132b8f41e8999417ba4b356c0c4857fa86fd463b0e271b13a084b13a10907 |
| 01-introduction.html | 17ae21237ecbe1dd8c9c595b2466017910f35386169f0c13a14a5b78569d1e44 |
| 02-tools.html | c1b73115bb26ca8b2ebbe7b305f28d5e5983aade05ca69df69f064285e4e7247 |
| 03-lexical-structure.html | 1b990cb52447379acdc611d5bf7f8ae247d49bffce6775e412a7cf9331eae30f |
| 04-data-types.html | 4e64677c4be318e08c307e7aa6da8f0cc2db4227db879e1f6b2dbd68b4c779f1 |
| 05-variables.html | 45411eb63f6035e2cac4943d9a329bdcd5c52a71f83bb523d0ba7a94f6c3af06 |
| 06-arrays.html | a707d5ca77923de9479b378ed8a37151fa9a1ede82118a76147a4cbfdb9506d5 |
| 07-expressions.html | 03f095bbb89c9c70b1ec94017a5f3d17561371c8e5846adf03b7a3511eb787e4 |
| 08-control-structures.html | 14f9e539ffb87791b1c48007a50935f906e043248305429db1d05d26556981e8 |
| 09-functions.html | ce47443ab92805864f51cff331cafd773428b2acfefd8204a9593e639f7f1915 |
| 10-prompts.html | b2df1b586e036f925e6287562541bdc3e733ddfa3bf4eb7de931a871b60abb08 |
| 11-classes-objects.html | 8cc076125e576c7b96ecf423fc081900cc22b8b5dfa02cba09e8b4e9e2f4e920 |
| 12-input-output.html | 94650091609ea096f14cf10e09e4d5ec808d058384d17d6de2f7974b8bd38eda |
| 13-built-in-functions.html | e0d76ab9e39165be9a778e5fece5e85a0434c3d8b47006f13cc6b82af690d6de |
| 14-graphs.html | f5e9b035b4e2e59baa6c110c79dbb508edf99147bfd12b70846c9a97df159e9f |
| 15-vectordb.html | cc8e259d778e2266c6cdf096f7d724ca9a241aa2251e463849a70b0d8736deea |
| 16-database.html | 1135d01b02ff20c2dba32d72f576928cbf6d1b212c64799e2d58b4bf8698b7e6 |
| 17-actors.html | 22b280db3a4b496a7e29f7bc7aec091b5a58c7be50d784e1acc9f2c4f289e568 |
| 18-agent-orchestration.html | 332f03510c4593493fc1b58c785d3c1a0978112a68b233688de684e6b6e77be1 |
| 19-graph-memory.html | 2782af3364b7d01edfb1f31bbe5f6aa4b63d799886c5ffcbbd405e537cec22dd |
| 20-mcp-server.html | 6811669af074756655e0232f75d93cecd3bc555ee2d15f3ee53cc118d8b2e2bd |
| 21-acp.html | 05bf83e84d7854c312bfaa8495c17dfc70c7d5e1e39fdaeb5e6ac15f4bd53e29 |
| 22-durable-workflows.html | 9eedd68c07d1f58fd2a8d0abc8b2d61e4b60b09b2eef91e4dbcc680558dffa7f |
| 23-agentic-runs.html | e4a638786fd6d70e83d17a604a24aa27d96cc76bfb946be7b33489e4f600f0e8 |
| 24-web-ui-hub.html | 5ef36fbdc02af952dcc298371f340ff825708aebf89e0099cb5266b48838794f |
| 25-web-ui.html | fe3c69e849c9a7352cfb04915b936653d6170ed2efa728a3ca32306f81277a64 |
| 26-http-server-html-ui.html | c5b32243ea9eaeb54fe662be014e6f04454522f0ee246a8ef43f708a0a438a87 |
| 27-browser-javascript-backend.html | 8f47cb14bed6e7240a477e4aea62d8f32c0cbe3904c816e839083653413aa19d |
| 28-rest-api.html | d44e3225e113ef50bee60293780211ccbded539e7db87611302f830a0d878cac |
| 29-rest-web-client.html | 91c4f2974480b1e6c9408dd5a94bbdb2f94250b2197d3e03b8c64ecfb25eb624 |
| 30-full-stack-development.html | ef406f4b47c393aaa7a15782213ca4e11c62d9693567a1a31343bd4da0f6c6e4 |
| 31-dotnet-interop.html | a09756c762f33db9fb544412a34066b3b4897e98ca57e04a24453fb26465af28 |
| 32-device-integration.html | 44abf76f864803c3a4421834ccf4f9eb9e8a0143ac1527d44cd3e9370bd89b3d |
| 33-personal-assistant.html | 4a13b0f9a2f90ddbf5b42a8ca003174ff55d0cb1bf43b8e310e0e835c77d0323 |
| 34-examples.html | 55b2ab4f23f27d6251b421afa9d297e657bc0e8aac09d842bdffa90c6f7a4c92 |
| 35-property-testing.html | 8b8f1b4211a99ddf19907e0746112427d1cdc0364b96a20c7cfa7bffb92406b0 |
| 36-grammar.html | bc8efe0893d5c73cdfa3f3a87ee6ef6aca7b531de0df189272472d24a7a94b22 |
| 37-appendix.html | 55f8fbdb667982c213dba3975bc1e56ec61e6b430d653a317e94ebd602813b25 |
| 38-appendix-gpu-billiards.html | ae43cdde4f4293bd3c48b67452c639cdc2bdac8da6a2d59ac79b5db41a92ea23 |
