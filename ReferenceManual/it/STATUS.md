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
| index.html | 9c22bb142e66b5e010301841c04aa3563f3be7b193fce68304edc90606c05d2f |
| 01-introduction.html | 4277e999f4419c929298cac575772e5fe997f6d07d832b14c5233d8e4fd840c2 |
| 02-tools.html | e05373d19eac61521210ca0a262a6127539e87725542d9d89572b38e39611a6e |
| 03-lexical-structure.html | 18b19e65ba027fd5a5df0cab886f976db2829ab93aa86d5ad05ca7ea89610629 |
| 04-data-types.html | 60e531876154f8d5ec1b040b174e452691b8458f04604033ec59981263fd7c1b |
| 05-variables.html | 5ea565b9a1fae17546bd43d4fe07b266d448eb6b51a00956fb322b60c504f440 |
| 06-arrays.html | 8da04793133599eb08d57dc59d96120424cb9c08f40f0a17fc3dd193cc561614 |
| 07-expressions.html | 39c761d2285cd01c1bad938ea744013f9f5ae3f5505cfa85c932031415277c86 |
| 08-control-structures.html | 0b6795f0ded51633ded09fa2966267edbc0a4f38415c6108cc745c82fee14beb |
| 09-functions.html | 29f2b8566f3d84eced0d003496f7be9debf237dbfab9023b14cf681ed0002f48 |
| 10-prompts.html | 0dd4ee82cb5583eeb04b2c9273465b6b20867cbc74a45605aced11cd0261adf7 |
| 11-classes-objects.html | 4f89a4aae573ba908080f2414041253bd16953be3641c3ee4f2790a5cf0d8821 |
| 12-input-output.html | 3e443ad6e503ea6d145650f930fe192ea079579706d3743e04edcee3b40b6283 |
| 13-built-in-functions.html | dcda48833d730c84b4a13f6e66e093efc592f19616bcdff89ced214fa7d22edd |
| 14-graphs.html | 3d95cfb276729b9eb6c637c58505da8b0d83ac1ab4fb6062f236826a25c2496c |
| 15-vectordb.html | 040841898e969cc9411397d8a1e7d8881670ac23a3b9283486855b72ae08d7f3 |
| 16-database.html | 7828b1e1fbb39efa46d214bfc5c0b2e67fa122b0d1a95f13d8c880721f0b65f8 |
| 17-actors.html | 0752d294656370a52f1bb44dc79ebc3a69ec570970fee9ac97ad66a72b144924 |
| 18-agent-orchestration.html | e3c5feb754ccb2011c76eb1c1eaf709ac85134145084b1683d05f0e0dd6fba08 |
| 19-graph-memory.html | 39c3ae65be241edb1f2a3d693b08daf45559d491e8fcfb3244d07c0700860232 |
| 20-mcp-server.html | 62947d35820d6707e7271653c9acf7514b7f015a38005159bd40518f69197e35 |
| 21-acp.html | a674c1773d8bcf03cea5cc8fb81820aee5340af05099db3dc66cb7ceb1902188 |
| 22-durable-workflows.html | 284d3e1232b823598ef8ed6bdb4a1843e3d1d0cbcc8171aa0fe92b65f8745b90 |
| 23-agentic-runs.html | 8ba10c2de1290d10cfe53565a9bd7bd1d9bf8107f0c9461e79425278ba418553 |
| 24-web-ui-hub.html | ed93346918a710398203768524327dd4706c5f49c210a2881ea469efbfcf61c6 |
| 25-web-ui.html | 3a74d13facca6d20a59d485aec68687757177d61669b51a3043b9fa34c2e6829 |
| 26-http-server-html-ui.html | 6198177cfd0c9dca3901627ae1c3461d4ddb23d86ae56ad18aa7ac1da043e888 |
| 27-browser-javascript-backend.html | 4cde3e0784f52df8dd3773b9d4d3527d925a856e7788578e023f11bdc1786f2f |
| 28-rest-api.html | 8bdd41546c9ce27551f8e2dd8d5fd7fbbc1550ce8a77b4bd40c9ad548f0e7536 |
| 29-rest-web-client.html | 92c9f0d9f165790a94d3c33a235b3d503dca3f3faf830950685ea8b21e8eafba |
| 30-full-stack-development.html | 5019c143ea62d10e135d8ef6fed90f84abb57bc7693dd06580cf0d7b6f691d74 |
| 31-dotnet-interop.html | 203596fbebf7253ef4d66c1ccb626348aeccec1aca18294b1884cfad40b6ec6b |
| 32-device-integration.html | 7954588db8828d2a8763b66013381c45bbebb10d518da2b3cdb28f98f917f56d |
| 33-personal-assistant.html | 52ab6419053a73a9e0144f15ee70b02efc95f9fb0476b85946c7b9fd0ccc004d |
| 34-examples.html | 847d6efc9e88d956f2152384554523deb3977dfe6aee5170365e879b15108d45 |
| 35-property-testing.html | b519146e6a752587559f57aa4616b27527e6493a34e8de084c420d4349a361a0 |
| 36-grammar.html | 36c5873f3d8ebe2bb625a092a7a11f659450cab0dcc3da3f49afe0d1ec12b2d6 |
| 37-appendix.html | 05e9026a03dd9f5e8cbec2cff93026617f4f91f201c8e625bfe510307f5deedf |
| 38-appendix-gpu-billiards.html | 24b5a03e85f4b3bf6c976aae397bc65d64e86ab8ec3b565965b616c25b37b8b8 |
