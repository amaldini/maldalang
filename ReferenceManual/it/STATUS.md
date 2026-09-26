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
| index.html | 529d8cd65f4578e3ed60ed4d89390f0f7b87a56b9bac686e921a5dae5aa3f04b |
| learn.html | 16ff75536685700c221ab5cce6b52a39f894c42d3e528c973c2b6f3adbafaba8 |
| 01-introduction.html | d0dea637cf76e8ff7d215342a5b49e28153bd6959ca3255121664a196f643336 |
| 02-tools.html | 3cc4cd6c60acbb2a3a2bd8ccca4b001ac52ccfb386f0fc5ee6ccc764d2b50152 |
| 03-lexical-structure.html | ee130164ca71f64c9fd95c703247628e61acf00ef749316a210e4b49e1fc99cb |
| 04-data-types.html | c08fe6bb3a0a96a0141f7b93859a9075d203c8d27785973ee53e4c30a15f6bce |
| 05-variables.html | c97e8b1b0ed90fafcb53202df5ec292981c7a3649ad140bbdfce6d7664534fd1 |
| 06-arrays.html | 9c82fd5fcbafc5bcefa0e65762e039299a5c636150efe0f3565cd7ebca3a959e |
| 07-expressions.html | 6919fcac0fc71bdc5711c41b67fc1a80c1efcd2909bad0e5eb0d7719b4337bb0 |
| 08-control-structures.html | 4c3cdfde0f4e17b688a0419bfc94271f47adef21608bfe4fec9ee3ef72d8b1f3 |
| 09-functions.html | 25ad5fdffe74e26ec55df7dad210bf4ec5137fa101edd2c70ccdd215093b9075 |
| 10-prompts.html | 3cb915c1af687ef61f84aec320c7130ca41d51e126c718e27dcb76e5d2b66007 |
| 11-classes-objects.html | 299f5f7deb9add4c51acf5f9c580b4e682dc622bf27818811f69e2f36a14e7a7 |
| 12-input-output.html | 84cc9ec1769799bfb4dc491d4d8614e598eabcc2c24ff83f9b0371229090a3ad |
| 13-built-in-functions.html | 1cc31ae7f3fc557761524377630296cdc5c87a4a15e44d31c18727a023f05712 |
| 14-neural-nets.html | bc88bd31d1c716e6222473528ee37a19fd5afcd75f260aa26fe90bbe57e9af0b |
| 15-graphs.html | 29eacf76dfffe1af9cfaf7ca364b6abf59872959e5b0d170a12994abac40accd |
| 16-vectordb.html | 74344737d42fb8e8457ca022823b119a4f0b8a9279ff411206acd216dd005a15 |
| 17-database.html | f83bdad57eecf1d2ac3c58f75dd996f13233bf9a9adeb3e9446905ceb6185f32 |
| 18-actors.html | e3d33191d184e9c931f4463a802c3a5d81e68008a54e689c053b82a40c8b90fa |
| 19-agent-orchestration.html | e7b5c40bd754959c56b03e0637fe81bf0e60efcca09835807b52bb49d1ec42fe |
| 20-graph-memory.html | b6b069f365aa0058e43b9e61c93542a13969b560009ba2118da411da6eb6f554 |
| 21-mcp-server.html | dd63931d6972a01b914db2895b7eb06406211432f4f46224c58e0d9bd98a689c |
| 22-acp.html | 284f06b9755451f5486e08ddc7ee5d67053a0ee5eb718f5ca1e77a48b61e27b2 |
| 23-durable-workflows.html | 3b790117e1a91c939c38dd2c14de2ed82f81376b1943726f091e498670949859 |
| 24-agentic-runs.html | 398577f9f61bbeb41096625c8fba1bfcda7c5578f8630d875a3b1d5b3a3b3805 |
| 25-web-ui-hub.html | 657cdfa8df32c376990b1defbd07db1442560371dd21e8b1fc2c6ed9307970f9 |
| 26-web-ui.html | 1ef835f2006cb5c086ab6993085effed969737fe0f01040ec95fb92d9a5f0b98 |
| 27-http-server-html-ui.html | 273bb4adbcecb33708e4e7f43d9245446148404ef78aa9b55d567c457270900f |
| 28-browser-javascript-backend.html | 61df35bd93dc3d8c7fb95cbefc76279c07d31055a09275fc6974ebf778419ff4 |
| 29-rest-api.html | c7f4808a6c24845f94210f13065a4b31b98ba22a6e79da99e9abfd8be85926ad |
| 30-rest-web-client.html | 5592dbc27ecfe2332bb422ebf8a11b4248c965417603980c5f74424edc9f40c7 |
| 31-full-stack-development.html | 2aa7974c7f3c5bdd229ef22d6284dbe6ea73fae3283525c8a65e6ab086c5a48c |
| 32-dotnet-interop.html | 0774c7a058ac268de6529142eab83a12e0f22ca76e9b942aa25b35caaa29cd96 |
| 33-device-integration.html | f2664750c8d520e56c8d0152c06b8a2c4994ecaf656c21312134676f271187fc |
| 34-personal-assistant.html | 1ae1348d29a0673d950457c0e8d9ced4b5e2a58a69894adbc7c6e19ba412d9a1 |
| 35-examples.html | d23b256b1e33241f25008e32614487a1e9212acb59d1a5d3c80dc303f9252447 |
| 36-property-testing.html | 0090c9a00c1d40c7ce07df48d69683ff2fc933fe564dacd7107f2b4f2f063752 |
| 37-grammar.html | 0e1a4e5c2958c7393419bb7d12185a1ed4bece8004f85328a42aa302e4b0e151 |
| 38-appendix.html | 1976be984911bda65555ac25cd74889b7acbfe1834be404974f73f47522153f9 |
| 39-appendix-gpu-billiards.html | 729843ee64d051e9a5da040cd42c0ce02cc710975bae6461f2de0ff8cad5ab4a |
