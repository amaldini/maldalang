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
| index.html | 12aef4a167119253af5e55c045a3dca4e594dd31610a4b3d85b380d32efe4095 |
| 01-introduction.html | 7e0b9d8ca2e4dec5d9fe14748ac4715f887d7aa36ffd930baa9e9de16723f5b0 |
| 02-tools.html | 1e3d8d4daa690a01e29083e16f5a87479419121446d50346b3b7d8e087fdae46 |
| 03-lexical-structure.html | 18b19e65ba027fd5a5df0cab886f976db2829ab93aa86d5ad05ca7ea89610629 |
| 04-data-types.html | 60e531876154f8d5ec1b040b174e452691b8458f04604033ec59981263fd7c1b |
| 05-variables.html | 5ea565b9a1fae17546bd43d4fe07b266d448eb6b51a00956fb322b60c504f440 |
| 06-arrays.html | 8da04793133599eb08d57dc59d96120424cb9c08f40f0a17fc3dd193cc561614 |
| 07-expressions.html | 39c761d2285cd01c1bad938ea744013f9f5ae3f5505cfa85c932031415277c86 |
| 08-control-structures.html | 0b6795f0ded51633ded09fa2966267edbc0a4f38415c6108cc745c82fee14beb |
| 09-functions.html | d807e16e112d6d8018ab6b4d0dfa7095494b73855b2bc148525561c2c33d170c |
| 10-prompts.html | 0dd4ee82cb5583eeb04b2c9273465b6b20867cbc74a45605aced11cd0261adf7 |
| 11-classes-objects.html | 1b886e6ab0201b249a195f4dc0cbedbe9a4032e6e9a1dd204452d2a8578bff91 |
| 12-input-output.html | 31846630ed2e146a17bb73bd9c7a605848db5fa549657cb61b1c0c704a75b146 |
| 13-built-in-functions.html | 92f2de2f652b66a898c200ef7aafcc611ecdbbe9ba52d6ffe68be7becec27118 |
| 14-graphs.html | 8a8958687b85196f2f83aefeb0a85251b1b56a2b2d382cda90d86a97f93a5d61 |
| 15-vectordb.html | 46d626ae91c81e3697a40e988b91f5bf825c10467343026c4ced605fd1e17baa |
| 16-database.html | 0d2a8c0189626afac04186fee9fbc1a504047a44480173e2e56cb55ea3886f60 |
| 17-actors.html | 0752d294656370a52f1bb44dc79ebc3a69ec570970fee9ac97ad66a72b144924 |
| 18-agent-orchestration.html | e3c5feb754ccb2011c76eb1c1eaf709ac85134145084b1683d05f0e0dd6fba08 |
| 19-graph-memory.html | 7455d7a934dd188eec58b939fde4a9dc6a5314060474368eef445f44668e2b22 |
| 20-mcp-server.html | 62947d35820d6707e7271653c9acf7514b7f015a38005159bd40518f69197e35 |
| 21-acp.html | 191b83d86302376a17defd24d2192e2b3ed668d4d2724d0260965fa3ea628dc9 |
| 22-durable-workflows.html | 284d3e1232b823598ef8ed6bdb4a1843e3d1d0cbcc8171aa0fe92b65f8745b90 |
| 23-agentic-runs.html | 8ba10c2de1290d10cfe53565a9bd7bd1d9bf8107f0c9461e79425278ba418553 |
| 24-web-ui-hub.html | 8f0548f659e56afd1b2389ac56e00fd8e74b1da3fe93e148ee3708b6fad6df47 |
| 25-web-ui.html | 441c42a8140f9dd1911a87a628b33fbcf434dfdd1b90b0a0f6959075b3116de1 |
| 26-http-server-html-ui.html | ed4858bd8445ea25479b6f5982aff4089a2d15c32863c6add5a8a97525697902 |
| 27-browser-javascript-backend.html | b7441ad282ce67c4a5c17d0e907afafcd2660550d4a6ee8800adfdaae7e8f718 |
| 28-rest-api.html | d85c53fe98a26736fe69377db7234c72dc43eeb1291f3ed8244a7cee904bd310 |
| 29-rest-web-client.html | 99eaa23bb58b40e6a4ab4505287e681876266b57fe75cd2147a3ef936e2c799e |
| 30-full-stack-development.html | 82db857e2a02086fb1b4213b3f3bb520e0b203ef0b377544d985e3a9dd980466 |
| 31-dotnet-interop.html | 4dc4e41c72410e564422e7a5b211a5550cb2b42662d3871fc0390d9828901df5 |
| 32-device-integration.html | 2bb56de06d0009d509f14c68ae659847fbb4e34f722405a5745a0a90f8dedf3e |
| 33-personal-assistant.html | ea95b4f54b0868a6d3784ad6d229e34b2efadb142e9bac8cbe5be3263166dfbe |
| 34-examples.html | ab7fb0b21c3c4ae34d49f58265e4e347e309885a1571cc37987f14b69e5eb717 |
| 35-property-testing.html | 7b3218a78ece7128ded3c21d58f16a3dff0efd17ee7fd25301ae5261fdb9dcc6 |
| 36-grammar.html | 1ac8483279add449493b4a172f2d380fc26dda5cb07f3ef092c764cad3c1f277 |
| 37-appendix.html | aeda7cf9d2b48ecd1908297d326d90d3539573791dfc3497621f5dcf7ac3fe76 |
| 38-appendix-gpu-billiards.html | 56dcd4d87a3b3cfb3ee3628bf4d4a3f95753fe95705e3f0c5f77a3a61dc3cdaf |
