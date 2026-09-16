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
| index.html | 65ba4d463d988fa5a242abd49bdbad27a9458f38981adf5ad6855430a3ad9a7c |
| 01-introduction.html | 694e5b9530676286b466e9ccff8231a1f89bf64599bb0f2a4b172a9d9eff3aef |
| 02-tools.html | 8ec7231730a5ca948b217528dbc004e59e96a5a98e78168d6af2b7066825e008 |
| 03-lexical-structure.html | 1b990cb52447379acdc611d5bf7f8ae247d49bffce6775e412a7cf9331eae30f |
| 04-data-types.html | 4e64677c4be318e08c307e7aa6da8f0cc2db4227db879e1f6b2dbd68b4c779f1 |
| 05-variables.html | 45411eb63f6035e2cac4943d9a329bdcd5c52a71f83bb523d0ba7a94f6c3af06 |
| 06-arrays.html | a707d5ca77923de9479b378ed8a37151fa9a1ede82118a76147a4cbfdb9506d5 |
| 07-expressions.html | 03f095bbb89c9c70b1ec94017a5f3d17561371c8e5846adf03b7a3511eb787e4 |
| 08-control-structures.html | 14f9e539ffb87791b1c48007a50935f906e043248305429db1d05d26556981e8 |
| 09-functions.html | ce47443ab92805864f51cff331cafd773428b2acfefd8204a9593e639f7f1915 |
| 10-prompts.html | 86655f50d9a57c761ef8bb8ef4851e8ddfdbad17939375b6f2fb1c0ac6ef5743 |
| 11-classes-objects.html | 8cc076125e576c7b96ecf423fc081900cc22b8b5dfa02cba09e8b4e9e2f4e920 |
| 12-input-output.html | 3ba7dc4e0a734129d90fa8c353aace39baebb72c098caa3ea9986de3dabb224d |
| 13-built-in-functions.html | c7ad21a01505e8a263d749f4f3e95651f42e01d2c86fbcf590e782e7772f7773 |
| 14-graphs.html | f5e9b035b4e2e59baa6c110c79dbb508edf99147bfd12b70846c9a97df159e9f |
| 15-vectordb.html | ae14c009892eeeff8dfee3d662e6477a17cce5ec86ff79f582c219e11d3e3e88 |
| 16-database.html | 1135d01b02ff20c2dba32d72f576928cbf6d1b212c64799e2d58b4bf8698b7e6 |
| 17-actors.html | 4159f0b4c8e43356d72cda0870ecaf0349de60fff6c1705f350c2b7eac236dc0 |
| 18-agent-orchestration.html | 884ee566dc3c41849b732e961948512ef1970c2a04cd46b2ac0b66ec50a8bf40 |
| 19-graph-memory.html | 2782af3364b7d01edfb1f31bbe5f6aa4b63d799886c5ffcbbd405e537cec22dd |
| 20-mcp-server.html | 6811669af074756655e0232f75d93cecd3bc555ee2d15f3ee53cc118d8b2e2bd |
| 21-acp.html | 05bf83e84d7854c312bfaa8495c17dfc70c7d5e1e39fdaeb5e6ac15f4bd53e29 |
| 22-durable-workflows.html | 5ea53f84bff7ee40e0b28313c09d1c832634f7a3b4c1b462caf956f5ae96321e |
| 23-agentic-runs.html | 2095e9462f3645750d6198e55b14cb94eede68d8bcfd5323746d397f2ed0a01e |
| 24-web-ui-hub.html | 5ef36fbdc02af952dcc298371f340ff825708aebf89e0099cb5266b48838794f |
| 25-web-ui.html | 21f88b63a160a67d51bbf187f6fbc481273e9b666a122e222490b5433f00ec67 |
| 26-http-server-html-ui.html | c5b32243ea9eaeb54fe662be014e6f04454522f0ee246a8ef43f708a0a438a87 |
| 27-browser-javascript-backend.html | 85d1b9995583896786078bee83166713369223567a4f91d7ddc2cc98c185553d |
| 28-rest-api.html | 467cfb0878116f1c8c24dd0b85e54d7f0b31ac60bcf6040fde6e101ce2db048a |
| 29-rest-web-client.html | 625334e7e23bc64c465528a84f65e8573694654887ba5bc7a4dab2a0f18b1e0f |
| 30-full-stack-development.html | ef406f4b47c393aaa7a15782213ca4e11c62d9693567a1a31343bd4da0f6c6e4 |
| 31-dotnet-interop.html | a09756c762f33db9fb544412a34066b3b4897e98ca57e04a24453fb26465af28 |
| 32-device-integration.html | 44abf76f864803c3a4421834ccf4f9eb9e8a0143ac1527d44cd3e9370bd89b3d |
| 33-personal-assistant.html | 533a255dc9a2dd3403518105d0d1ddfa04e13c0769be7bc2642f5e3394491267 |
| 34-examples.html | cb1427c8b361ea8a7a8212e3bb6ae056ca08c387ce8fe75698d91965cd14f60a |
| 35-property-testing.html | 8b8f1b4211a99ddf19907e0746112427d1cdc0364b96a20c7cfa7bffb92406b0 |
| 36-grammar.html | bc8efe0893d5c73cdfa3f3a87ee6ef6aca7b531de0df189272472d24a7a94b22 |
| 37-appendix.html | 1b1812a3af708758705be75882172e27b8feb53d6e8c8ae9dc8c609e54e22d39 |
| 38-appendix-gpu-billiards.html | ae43cdde4f4293bd3c48b67452c639cdc2bdac8da6a2d59ac79b5db41a92ea23 |
