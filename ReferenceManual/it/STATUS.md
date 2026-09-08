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
| index.html | 1ae14cf01be60a2a9d1b872bc66a608c17d7308000cdc8812820b266beefa35b |
| 01-introduction.html | 0d72974c0f5617880f9952f3f08f7b24b7f8f6b62ad0488976afea628ff4320f |
| 02-tools.html | d6e89633a814014a9b88eeeb37572588a19a3a83ed28dce4fadd0cc9cfee1e78 |
| 03-lexical-structure.html | d8b6df8d945f7e025a31763a43d94e6c67ff60f4a09c3ffdcc1e5e130ecd769b |
| 04-data-types.html | 93663e571d109c246ffb784dc9da77d39fb28baeba88881763197b8ca547cc31 |
| 05-variables.html | 804bbff2205685b446fc553e9a8c24f13955e5e7cecd3c823f92c782be76846c |
| 06-arrays.html | abfeeedd3d7037d4fa35e43eac4b70daab07bc2b3544b0b5f7bb14ee359ccfe6 |
| 07-expressions.html | d1239766d8726be37a9ed9519d70e0f31a368938dc7dd48ca90082fcef9ef8e8 |
| 08-control-structures.html | 2d2da294bcfd79d4141b5adce0cf19230013c845507317253288f5c12b1e834d |
| 09-functions.html | d7dd0e77247a7cb977b17a79d48ff7238907a0217c9540951f4bcf0851b2f6ea |
| 10-prompts.html | fd54f2938343dbbe090e467b2a5d61a717d21b42a5bbdc75d83e36640de6d261 |
| 11-classes-objects.html | 9cf47bc011d320827b3fa34564311b35189f5da617ca5cfb6e47090fe17ac285 |
| 12-input-output.html | d8833f360e954877fe0830bc173ef31d942bbe183d38c372a47bcd9e2015997d |
| 13-built-in-functions.html | 2c524705122ac46b2c176b99263931cc8e920eb9c3f2ea5baa391039163bf7cb |
| 14-graphs.html | d8394f4d28f40641f61ac4975cb4adaadab97fa727b7cf7e21e727c17fbca02a |
| 15-vectordb.html | a566d54c08929763aad3d8afcd14a2f24bd24f76abad88a65f10ce3d099dbe01 |
| 16-database.html | 216343151a38d0927cab67658ab80a85c397ef99aafa0f4db9dc7484e205236b |
| 17-actors.html | c23407c1e88b34a26a4718de72899fe74e0e6bfca9caf1dd5db8c3ebfa2ad14d |
| 18-agent-orchestration.html | 689d28b60e5ae4467303c71e928e8dcc028b78aa0e032d2be4721ac4caa0964c |
| 19-graph-memory.html | 95d2a2846598551639c0d300c7083488b414128e53944084f1e9ce82e496be67 |
| 20-mcp-server.html | c559ba7d5e8e0571e2ff787a7ce0d28ddb5f9a4b36a120406f3b7e28ba38b9f8 |
| 21-acp.html | 364f4e38abcdfdc46187abd1165fdb5ca604d98790c3f60937fd793fb159dc8c |
| 22-durable-workflows.html | 86d5d169f46399e29c0676dc6126417b7489cd7f55aad267cb498a5a18bff392 |
| 23-agentic-runs.html | 1b9326a1ee19a14badceae04969cc22c3c3c1ec713222b6e42e11291d772bb89 |
| 24-web-ui-hub.html | ee6f7b96a8855a0df0c4278f5e686f30e557519895d10907ad217f9af23eadc3 |
| 25-web-ui.html | 66c054bb25f73469b4d153bbde385b2527dcda97fc3b8184b21cd41c91117b31 |
| 26-http-server-html-ui.html | 9605c4a3ec4ecb65d94a6b80fb529026d313b2091af651e947748bd97afc8b04 |
| 27-browser-javascript-backend.html | 3c7a098900cbdfae3c888ab4838bc57f3b30ee14df62128306e1bddc92b8673d |
| 28-rest-api.html | 90401a5840b307f71dac5f920c2020e1ae089619e8ab1965a9b3070c20f920a7 |
| 29-rest-web-client.html | 2e55162d4134c41b5f0f682439dce4100e68894829b93554d2e875b6428a11c2 |
| 30-full-stack-development.html | 5ead312940873a74e99d7161c522cf56e0d1ff8f5eae88fef13279d9a5e78bf7 |
| 31-dotnet-interop.html | ed7cd52ae60a8f6ff6de0e3b62f6baf60c67967f4e2eb6bd111c9905ab8a9efc |
| 32-device-integration.html | b1c523a96e438993c014f0b25c1a7b81a98d7de0fbc9fc6843a6fa0646ee028b |
| 33-personal-assistant.html | 8061de280244bc91c75555a9af82e4ea4f3b12d1fe2e674f3cf45adc78076ba4 |
| 34-examples.html | d8aab1f981fd57c3a43674e8e58a7a0c4648d946b81d9bb1d214cf3217603232 |
| 35-property-testing.html | 71c4cf2f517ff1a64ed44f3f8eefcdfd79dde4fb22faae6301dce386a1b442d6 |
| 36-grammar.html | eaf2e348fd08314a3ab0c8bd7b7a987da6934b39ba41b5e99cb039b8a339ce89 |
| 37-appendix.html | 0abd42ae2b4313154df06a8355b16ecac8186d0a5996f3bf7ef2c1306c4f6256 |
| 38-appendix-gpu-billiards.html | b7b137af4abb80862a504b5d40724d77289cf4a20b405f0c8a41d743cba6edd1 |
