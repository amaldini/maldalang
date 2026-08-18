# Italian translation status

English in `ReferenceManual/` is canonical. Each row is the SHA-256 of
the English HTML this Italian page was translated from. After changing
an English chapter, update `it/{file}` and regenerate this table:

```bash
python3 scripts/sync-reference-manual-it-status.py
```

| File | EN SHA-256 |
|------|------------|
| index.html | 23f321deb7dbcd694841d92264e4fe0cb67974ae1c31f43a59164f62107f4263 |
| 01-introduction.html | 4ecffa92aea73972d323bd81e3f6e265e392dc0be0e0f074359b97dcd3ac63fa |
| 02-tools.html | e3656d031c92566d54a0c0f4b22b49c4d93075df30dbcddf7b6e9fb2b5817755 |
| 03-lexical-structure.html | 15c163bbf136f0e1ef370d91ddb5685b1fad91e4e3104b1a464fb4b8b1ae19ab |
| 04-data-types.html | a18934f8dd566e496301da24d95dd7c9abf964fc052e4f2a3ff828c812796c5f |
| 05-variables.html | 343e0019a46c06e5eeafbad86ff6b3627becfbcc32a44a6347304e5c156fd64e |
| 06-arrays.html | e518b42217b8188e0cf44c98a53b26a00966ed8c055c9155373caf3540144e1f |
| 07-expressions.html | 3ab9b9eb0ef45f78ce2150a31d18cf4390a40f71d5a0e3b29fba25e747cde3a6 |
| 08-control-structures.html | 238dbb47e3c1ed4758bb183c7d4e1cdc735ef5836e262ae7bfdc11e8ad051cde |
| 09-functions.html | 4b521511b3ab3187d2b1ddfac4f9ab411aec6c63fc6886c0b17015386ada25fc |
| 10-classes-objects.html | 4a6603915f79e577dd3877073d7e4d171aedf41307ee3dfe5dcb22726537db64 |
| 11-input-output.html | fb9944ef63140806cff1120dbda34c0d2c11a180bf5af79119b78e45ff680d24 |
| 12-built-in-functions.html | b96223f2d7617398542c40eb78dc903be2febafc03c76d0a824a22dd9898902a |
| 13-graphs.html | ca994d16c8768a70f4d09463e7d7e86d854659fba20ce0e8de3cfdb6a11f1fdc |
| 14-vectordb.html | 9ab07f7ee02949751a0bc0cc077940c86bbe666cf475bb988f9fa79d7d7bcb00 |
| 15-database.html | b1200e7f37f177b9fcb950d32245d3865e89f7a0b6581a9abbf2e5df2eb4297f |
| 16-actors.html | 11e64781c8eb21cbfaaf63fcad10fb2ffbcb5f3b6d19353935cc4332c5facb43 |
| 17-agent-orchestration.html | 9012d2edf602dcd6590762e64aabb9f17fb4da00f40473b99825b90a2cb37fda |
| 18-graph-memory.html | 8d20ae9cb60c40e9a4d2db1723cb3af30ab753f004d534ef2a9d56e754727a1a |
| 19-mcp-server.html | 65ceb0bc0042628a8713181d35a4805e54f3776d5cbf90848cacd41268018e32 |
| 20-acp.html | 0e9e1a1991854bb8f2d060cd3a068140a19b958ec235086600885a8140cf60cd |
| 21-durable-workflows.html | 17e85a39229ea0e8e26424c5b005ac67d3c337e829e3b7adcfa81b65d39c035c |
| 22-web-ui-hub.html | be0fa045fb0418d23ba9b8998265a5254da93c09c17f0557167416a14818d2a1 |
| 23-web-ui.html | 8e5a65a184f688380cf9bb242b2102f0f24b0388c6a2d9455125d9f8504bff52 |
| 24-http-server-html-ui.html | aac5c77245dc254a5f1ae5e6f4cda4e5bf004623b00482523d6ec53d059c6550 |
| 25-browser-javascript-backend.html | eb8ec7edfe59576bcaab10a1195dfae4f8414d7420deca76150cbbfc0ff36988 |
| 26-rest-api.html | e492c2f4fbec6e8b480f511e4f21d0c03db910081b4f9991f71ea18438687a2d |
| 27-rest-web-client.html | e3e0c6fa00197fb0457ab2e0be7ead13b37ca15169d7aa087267819a43a48ac6 |
| 28-full-stack-development.html | 1197b9016b10d3ac86ead78d2c24a494f874ebf582b0ea820029e96c85d25790 |
| 29-dotnet-interop.html | 3688e6dd04fb2fb9e56133c34f9c712ca6e37ca61772fb27d77543dc2a2868cc |
| 30-device-integration.html | 84048c4e9c5b5afb474bfb2f11d624740b810804a72658265daef8eb594c8ef9 |
| 31-personal-assistant.html | c1ef1589540accbd4ab17f529fe1fb28a35caf68b893424f7998241faf661714 |
| 32-examples.html | d8ee2d390b087bc032475fa553d026f36b83491e2784327bbc43d2eb98558623 |
| 33-property-testing.html | 20343fd7be03ef6625daea7c86050cc69e2aea1e312cb8e725002c19d5aac720 |
| 34-grammar.html | 63f093a1cf2fb11727d9c49cc5a6aeb436b3a52771408008bd9a0465c0c07c70 |
| 35-appendix.html | 3b8c232ec7662431614990fe9d1928536af66ebd82e1c46ceee16433ce1f418c |
