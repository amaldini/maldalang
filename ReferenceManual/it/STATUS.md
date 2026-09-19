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
| index.html | 14309afb5af9129d2da6af7cb098ef132c50b37c5fd0b35be5eee4537040ff9d |
| learn.html | cfa802b9e98fecbce1042fcb1cabe30cb418ad64616e46176c8de8a90271924d |
| 01-introduction.html | b039af664bfa87db34b4612bac1d794e041184108ab903bbf2805a22e4a0f227 |
| 02-tools.html | 6a1004c1370f2bc654c7a2d90016c6a182b1acc7095609ea8c67815847280cb2 |
| 03-lexical-structure.html | 3da2779adff2ae76307c1404a3b4b6e0bb18c7c1d25f9b3633034443552a6882 |
| 04-data-types.html | ead2a9263370d7eee4d99e36666bad701e5e0304529b4256a985a6e1c7e75f1a |
| 05-variables.html | 8d1893fb5aad290a665de2ccace5241f9e12c3f325d9fa40fcbfddc2b3ee8349 |
| 06-arrays.html | 762b978d87995b0bb22f14cc24b042ceaf40323dd5f834320502ca82ea25754a |
| 07-expressions.html | 5e452b1ff166706b3965cefb23e50ee2d292154a987f69bc8b0ac16b9ee40df9 |
| 08-control-structures.html | 04d9bac59137c4ad8b03ed4f62d64c70409653d06d3863c9c8bef8e48eeb7f0c |
| 09-functions.html | 871c09b648987e481ee7ee0684df18955a086a2119f20c133c44c03f0917ef30 |
| 10-prompts.html | b4dd0df0d32c6685abae42f4991072d13af3278c0565c7fbf625cc2dc2496cda |
| 11-classes-objects.html | de5870556b25ff2094c83aab20420a8ebd7b73ef4841c472b39b03db60e95ddd |
| 12-input-output.html | 76af4bec969d742bbfedc1a5a4d48a7231e33008cca53bd7357198373d2a4b9e |
| 13-built-in-functions.html | b1609d70405095210b1883d457efe8922d2404538ec5cb7af12c37d77be8d8fe |
| 14-graphs.html | 63720477b6804829568c8e569a7d88c9c55e0d1f2750261d063cb9cb1886ae8f |
| 15-vectordb.html | 8143ab2119c47721a75f765e9184075b6639a1eaf154397bf976d1ed42cbcbb5 |
| 16-database.html | 1135d01b02ff20c2dba32d72f576928cbf6d1b212c64799e2d58b4bf8698b7e6 |
| 17-actors.html | 399be5c6dc84da917c94cde6d06ea3b285b2a6ffeedc1358177b6b6f23f63de6 |
| 18-agent-orchestration.html | c221a0f8627af069e2b7da9a26fae82aba83ba8a4d4c921ba920d0e87b938368 |
| 19-graph-memory.html | 2782af3364b7d01edfb1f31bbe5f6aa4b63d799886c5ffcbbd405e537cec22dd |
| 20-mcp-server.html | 6811669af074756655e0232f75d93cecd3bc555ee2d15f3ee53cc118d8b2e2bd |
| 21-acp.html | 05bf83e84d7854c312bfaa8495c17dfc70c7d5e1e39fdaeb5e6ac15f4bd53e29 |
| 22-durable-workflows.html | 88a9b6ff8796cde0b0620caedea1ded803962295455beec56024af41a62c4cc9 |
| 23-agentic-runs.html | e4a638786fd6d70e83d17a604a24aa27d96cc76bfb946be7b33489e4f600f0e8 |
| 24-web-ui-hub.html | d2476e4fe877dd782d755a3b96b15f5b7306fc9de6b1949a4b3007c6aefb2479 |
| 25-web-ui.html | f895eb5df3f091af5ad9926e032940e708641b018fac356953c20d40c8c43791 |
| 26-http-server-html-ui.html | da285bc50689b9f5cff218bf3826c46bad3239c06e106564c983a3e3ea1ff327 |
| 27-browser-javascript-backend.html | 3cf06441ddced79637d16152ff34cb927ab233aa681edf50dad509ea506e1968 |
| 28-rest-api.html | 19bf93f14519bf97ad8eff8dc51794b8385dee25fd89eafa20c7c0941d91cd6e |
| 29-rest-web-client.html | fd7e75bd146f273a293bd3f5f391b58d8cad1048f2fee434aa332bcbf80a98e9 |
| 30-full-stack-development.html | 03a011a3b73e9f9abf4b498fed7eaeb230dbe26c17920e23d9d146c9306c8461 |
| 31-dotnet-interop.html | 06aed0859ea49b90a656c1c427ec64062e00e51aa63cc3070264ece3d0247ba8 |
| 32-device-integration.html | 44abf76f864803c3a4421834ccf4f9eb9e8a0143ac1527d44cd3e9370bd89b3d |
| 33-personal-assistant.html | 4a13b0f9a2f90ddbf5b42a8ca003174ff55d0cb1bf43b8e310e0e835c77d0323 |
| 34-examples.html | 303cb4e536ecd3f8e66d12a1499cbd78824361b2a909df1051360c42d5d0c37a |
| 35-property-testing.html | 8010e4cb701eb6841c5b8b159088f81f56454bc9eedde2323299a25e8a6d1381 |
| 36-grammar.html | bc8efe0893d5c73cdfa3f3a87ee6ef6aca7b531de0df189272472d24a7a94b22 |
| 37-appendix.html | b533631300e789165d340361096bbdf96ac27df784b5778e91cc845c32d31f9f |
| 38-appendix-gpu-billiards.html | 40d3d5376471d827c9705df2c3112b8119191e8f4f5f7ce152416158e4123f99 |
