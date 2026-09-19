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
| index.html | cc69acea67b513172fe21d78c064c0138d26b78498399bce7f29321155d2993c |
| learn.html | 3e712a7bd78663d24ff45a1cdfb47dfb531c302db9466eee7f2468bce6ce263c |
| 01-introduction.html | b039af664bfa87db34b4612bac1d794e041184108ab903bbf2805a22e4a0f227 |
| 02-tools.html | 3639b8d6a126fd5dd10746faa92a0a454e7eb94b234a51bd0a4a56c76e03ca75 |
| 03-lexical-structure.html | 3da2779adff2ae76307c1404a3b4b6e0bb18c7c1d25f9b3633034443552a6882 |
| 04-data-types.html | ead2a9263370d7eee4d99e36666bad701e5e0304529b4256a985a6e1c7e75f1a |
| 05-variables.html | 10497d78b519fc06c0f1bdde4043c05aba8ef3041ae866d92eebdf30fdf8aba0 |
| 06-arrays.html | 762b978d87995b0bb22f14cc24b042ceaf40323dd5f834320502ca82ea25754a |
| 07-expressions.html | 21cce6928f34d7e65533a57c547ba3d22e4989b2910c43939d0e43831be4f8c7 |
| 08-control-structures.html | 71b29003f597c328b966ae008a3f7a62de54a1be80c8fa8659904eb55837ab10 |
| 09-functions.html | 871c09b648987e481ee7ee0684df18955a086a2119f20c133c44c03f0917ef30 |
| 10-prompts.html | f976053e22d575d899be77769e1d9fd02bd53359a51b7a10d63e0dd45e8b3f53 |
| 11-classes-objects.html | 3a1fc9fc6be4acf09f9883878d1166e7264ae308343dd125edae38bf7aa25740 |
| 12-input-output.html | 76af4bec969d742bbfedc1a5a4d48a7231e33008cca53bd7357198373d2a4b9e |
| 13-built-in-functions.html | e0d76ab9e39165be9a778e5fece5e85a0434c3d8b47006f13cc6b82af690d6de |
| 14-graphs.html | 63720477b6804829568c8e569a7d88c9c55e0d1f2750261d063cb9cb1886ae8f |
| 15-vectordb.html | 8143ab2119c47721a75f765e9184075b6639a1eaf154397bf976d1ed42cbcbb5 |
| 16-database.html | 1135d01b02ff20c2dba32d72f576928cbf6d1b212c64799e2d58b4bf8698b7e6 |
| 17-actors.html | 399be5c6dc84da917c94cde6d06ea3b285b2a6ffeedc1358177b6b6f23f63de6 |
| 18-agent-orchestration.html | e620cbcb0378f9cdb7b3d6a78046ece517def3cff757c742c47fbfd6dc943e25 |
| 19-graph-memory.html | 2782af3364b7d01edfb1f31bbe5f6aa4b63d799886c5ffcbbd405e537cec22dd |
| 20-mcp-server.html | 6811669af074756655e0232f75d93cecd3bc555ee2d15f3ee53cc118d8b2e2bd |
| 21-acp.html | 05bf83e84d7854c312bfaa8495c17dfc70c7d5e1e39fdaeb5e6ac15f4bd53e29 |
| 22-durable-workflows.html | 14bf91d4c9dda436d1f998b515a6ad42e65abf2cb28cfd5b1fd8dd358d008ef0 |
| 23-agentic-runs.html | e4a638786fd6d70e83d17a604a24aa27d96cc76bfb946be7b33489e4f600f0e8 |
| 24-web-ui-hub.html | 2c0d3a23b221da510cd4097f222e846b52c23382f5698e3df50a2dee0a3388aa |
| 25-web-ui.html | f895eb5df3f091af5ad9926e032940e708641b018fac356953c20d40c8c43791 |
| 26-http-server-html-ui.html | da285bc50689b9f5cff218bf3826c46bad3239c06e106564c983a3e3ea1ff327 |
| 27-browser-javascript-backend.html | 3cf06441ddced79637d16152ff34cb927ab233aa681edf50dad509ea506e1968 |
| 28-rest-api.html | 19bf93f14519bf97ad8eff8dc51794b8385dee25fd89eafa20c7c0941d91cd6e |
| 29-rest-web-client.html | fd7e75bd146f273a293bd3f5f391b58d8cad1048f2fee434aa332bcbf80a98e9 |
| 30-full-stack-development.html | 03a011a3b73e9f9abf4b498fed7eaeb230dbe26c17920e23d9d146c9306c8461 |
| 31-dotnet-interop.html | 06aed0859ea49b90a656c1c427ec64062e00e51aa63cc3070264ece3d0247ba8 |
| 32-device-integration.html | 44abf76f864803c3a4421834ccf4f9eb9e8a0143ac1527d44cd3e9370bd89b3d |
| 33-personal-assistant.html | 4a13b0f9a2f90ddbf5b42a8ca003174ff55d0cb1bf43b8e310e0e835c77d0323 |
| 34-examples.html | 45f238033fc9ed78d42a698603e9727c495c0b2958dc8449d179b8c786c140cd |
| 35-property-testing.html | 8010e4cb701eb6841c5b8b159088f81f56454bc9eedde2323299a25e8a6d1381 |
| 36-grammar.html | bc8efe0893d5c73cdfa3f3a87ee6ef6aca7b531de0df189272472d24a7a94b22 |
| 37-appendix.html | b533631300e789165d340361096bbdf96ac27df784b5778e91cc845c32d31f9f |
| 38-appendix-gpu-billiards.html | 40d3d5376471d827c9705df2c3112b8119191e8f4f5f7ce152416158e4123f99 |
