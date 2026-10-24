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
| index.html | c596959e1317a5f56f1fefb747c41faa9267581d7a7dd04072a8a32af91061b3 |
| learn.html | daf341c8086f75f26d6f39026aaaaff1ce35fd4e2cbb6f3862f7578341e627f1 |
| 01-introduction.html | a0677ff971b6e8eca9d5eeb38dc2165f7084b9f3c7cafd203bc3025d9d19aa62 |
| 02-tools.html | a7aff24176afca55dcf4a68139ed9411d63018ccbcae26a506fc8e67d51ca98d |
| 03-lexical-structure.html | 128dbe2e723b7d8260079709c08253d19efc7d4e1b263c3459a435e359da4b57 |
| 04-data-types.html | 8fac006fd3c2ffca16e533d5fd32841164fb04afc82ed0a9067e5337c3f4cc52 |
| 05-variables.html | f071ce860879a61c8d957091a4e10e9ae8b4f253a08f0c95dfd1af9a8eaa494f |
| 06-arrays.html | 79694ee187841941f1e8342fcfa1d8ba066ffa315cf85aca145cde951140768a |
| 07-expressions.html | 27265aca23aa4f1369f4b9c91810d47d25e105e927cc52dc1ec7f303029b3793 |
| 08-control-structures.html | 2d717c98e142dc45f43563d31200ab5ced9871c86069185ad0d8386fc7ee4ce1 |
| 09-functions.html | 1ba603d6b236878766faf341990bad2030c3fdf247be4f23dcaa3f99080991c1 |
| 10-prompts.html | ee7b37d3f8b4ecb26ce0a6cd714ef7bd4831703b88cc0e48fd8c4738d2a56cb2 |
| 11-classes-objects.html | 4cf0a64ed5546ccc3af449b41d8f87e2b6a6e4a263b7ee7a1bf0ea0af6ed87c1 |
| 12-input-output.html | 238c4342525321cc5f641fcfdd7676dcf213a87c7252fb8fccbb2e3d10d5b5db |
| 13-built-in-functions.html | f783601abba816cd64d70d6a81ea2f89d8c33b99b6a9ac1668765c89d0cd0ced |
| 14-neural-nets.html | 6f2fce1b4f0ff961e05f2ce8f261de9202db981b8f9d0a0522cd028243d9f841 |
| 15-graphs.html | 405568a4d176fda11393c4cef7ccfa3f53df28bcadf54001400d224c89d699b5 |
| 16-vectordb.html | f8b8d297b3f0c3fac61891f3ae5f321975ad0a5fdd28dad33a60fc60f4405454 |
| 17-database.html | 56cc7c7204c851928ff9aca0d928fa314766bacc3fba5fb66cb53e77c6e4211f |
| 18-actors.html | 130c071fdb01aeb42bbd5b82c7aa3863758e9b428a72bf33bb4f9fc39b9723b8 |
| 19-agent-orchestration.html | 7ae4f26fe8e4e5fe7d905d1b5dd9f5a994a73e01161bf5cad455074c476fe35a |
| 20-graph-memory.html | 2d62a295b50ba6b37b701b46759cac3dba27a32a587332f47afc943fbb40c7b6 |
| 21-mcp-server.html | d7199207e18dbf816f9f27926199c1db019c6684ffc8f6b4f61ee9dc9427bf21 |
| 22-acp.html | 9c6bfec3fe471332721d84fec766dcfb60e0cc154d3c04a82946bd9c653950fb |
| 23-durable-workflows.html | df03cb41bb5689d064e9eda4dff13a6b792cf70305b01f804d468d43f2d3b2c9 |
| 24-agentic-runs.html | 1000af3f95a36a05f760a4ae3dccd20d324645610ddd51a69ec4322d479bfa23 |
| 25-web-ui-hub.html | 76e211825e324c24a54677e9f3b89fba7218d67d435986a84d3e2242ae258c1b |
| 26-web-ui.html | 6db2b996f2a0ebfb1a8e8e91b96b1f55cafbc5a1fca6de8d4a805892e5365f7f |
| 27-http-server-html-ui.html | 6b735cd119bb36be0d74e77f837b3325e78925ef6f8d98d79e97fd853cee75d8 |
| 28-browser-javascript-backend.html | 271fcfd22edc49265605e207f3c94c976007ef6ed95b6667d58c536e1d407b80 |
| 29-rest-api.html | 402f049ea17fd83036171156e2f7175154b74bc427161fe477c0801cc693c4e1 |
| 30-rest-web-client.html | f5d0ae7875b6674be2b76ca56f81f9bc1d07d46aa457304a5877fe4c9d3cde83 |
| 31-full-stack-development.html | 8bbb0756a472059508b0f0b417442f82304419ee0ae3890ea7b5351c02ef64bf |
| 32-dotnet-interop.html | 799f5139ecf67f7b2e3d0bec22dccb69b529dcbff3c9eab3213feffcebb8d935 |
| 33-device-integration.html | e470491c1ef806112450ea2381c6856a83de44dd38c641e499d1b99da38ef669 |
| 34-personal-assistant.html | 3ddb2a1132bde983b747bb47e7488a7d34e2602b834b8e99e9209e2d922f3cc4 |
| 35-examples.html | bb3696f1eb22c630fd71e2db9d2f944792b9987109e10892b16a36c248125c69 |
| 36-property-testing.html | 9ad258b88df3cfdcb2f2cee32e126fbc14f895f2e5c5b9e3ea5b23d6e9c38df9 |
| 37-grammar.html | 5dd4e9e2db34d3828d6aa0a5f350a0c0523a3411b67a5fe6e77be45eb22a8539 |
| 38-appendix.html | 838563eb8e07ab04a29c670af82b118baa62da469c2f9fcd9b788060515c49ee |
| 39-appendix-gpu-billiards.html | 118bbd78f3fe96f287f1921a70c962e6d32bf0f3ea4f3591e0f0d1c22745b3ea |
