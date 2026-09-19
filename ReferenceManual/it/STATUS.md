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
| index.html | 901b05202ed5300a69498c90a855639c31f3cf89736cd6730d2d27605d295124 |
| learn.html | bf771b437fa4fbebac324b7871c014e2225deae51386f721fa661daceadf7110 |
| 01-introduction.html | b039af664bfa87db34b4612bac1d794e041184108ab903bbf2805a22e4a0f227 |
| 02-tools.html | 6a1004c1370f2bc654c7a2d90016c6a182b1acc7095609ea8c67815847280cb2 |
| 03-lexical-structure.html | 3da2779adff2ae76307c1404a3b4b6e0bb18c7c1d25f9b3633034443552a6882 |
| 04-data-types.html | ead2a9263370d7eee4d99e36666bad701e5e0304529b4256a985a6e1c7e75f1a |
| 05-variables.html | 8d1893fb5aad290a665de2ccace5241f9e12c3f325d9fa40fcbfddc2b3ee8349 |
| 06-arrays.html | 762b978d87995b0bb22f14cc24b042ceaf40323dd5f834320502ca82ea25754a |
| 07-expressions.html | 5e452b1ff166706b3965cefb23e50ee2d292154a987f69bc8b0ac16b9ee40df9 |
| 08-control-structures.html | 04d9bac59137c4ad8b03ed4f62d64c70409653d06d3863c9c8bef8e48eeb7f0c |
| 09-functions.html | 871c09b648987e481ee7ee0684df18955a086a2119f20c133c44c03f0917ef30 |
| 10-prompts.html | 472efcf2647ff7a847a11badc07ae223e28794885689b44529de9853cb0b0f07 |
| 11-classes-objects.html | de5870556b25ff2094c83aab20420a8ebd7b73ef4841c472b39b03db60e95ddd |
| 12-input-output.html | 76af4bec969d742bbfedc1a5a4d48a7231e33008cca53bd7357198373d2a4b9e |
| 13-built-in-functions.html | b1609d70405095210b1883d457efe8922d2404538ec5cb7af12c37d77be8d8fe |
| 14-graphs.html | 63720477b6804829568c8e569a7d88c9c55e0d1f2750261d063cb9cb1886ae8f |
| 15-vectordb.html | 8143ab2119c47721a75f765e9184075b6639a1eaf154397bf976d1ed42cbcbb5 |
| 16-database.html | 1135d01b02ff20c2dba32d72f576928cbf6d1b212c64799e2d58b4bf8698b7e6 |
| 17-actors.html | a93e539e5534a829a2061365bfa9fb5275739f508b1b2d7ca076f40d8fea24f3 |
| 18-agent-orchestration.html | c221a0f8627af069e2b7da9a26fae82aba83ba8a4d4c921ba920d0e87b938368 |
| 19-graph-memory.html | 1370eb9ad7632c0fa77df833ce504891e7b902b7819b41b0aaa7d184493fa70d |
| 20-mcp-server.html | 93ce07e6d000c80f9dead3c4357443c18f546c17a10c0ba6e09be3a744a974ed |
| 21-acp.html | 36cfa2fa8789741cd5346ced3eede2e7cf2c9c58c43bb8e96f643ab0b87e3530 |
| 22-durable-workflows.html | 88a9b6ff8796cde0b0620caedea1ded803962295455beec56024af41a62c4cc9 |
| 23-agentic-runs.html | 7764e57f748e10edce17db6945ec3a35bddf2d5adf591bf08fb2b48794cc8e22 |
| 24-web-ui-hub.html | d2476e4fe877dd782d755a3b96b15f5b7306fc9de6b1949a4b3007c6aefb2479 |
| 25-web-ui.html | 11c0b6f39a9f16053d49438a24ffbccd2ef591c5008855f1101d97e87ff551d3 |
| 26-http-server-html-ui.html | 4c9eb4592ca5dab0027be633921dc51d03eef2029fae6e1e02d0e004104196d3 |
| 27-browser-javascript-backend.html | 946e2952e4ca2bca6131a79abc9f7a025ad31b3b9fbafaff56cb7d18203b7221 |
| 28-rest-api.html | 315762fa25f33168474e39c5cbd4356b19c7f0be6941155491f32132858b753a |
| 29-rest-web-client.html | 8729cb3db40ca4f5a8d99e40e78b016d63691164fdddaf4cd56f10ce16a2fbe4 |
| 30-full-stack-development.html | f23e7c492128425b1dd3dec36e7f8ba38ef4b5942bdd932c985f30c8ab03ddfe |
| 31-dotnet-interop.html | 64c4fe09a84f130550be9a9d4ad5cb8a59469e465759b5a03963bb4f22209fc4 |
| 32-device-integration.html | a6f707150052c212aed969436eadde0d0dc3b495b90e6f8ea004bd37517c5286 |
| 33-personal-assistant.html | b106fb198fc8d5af3e918473a59d53814357fd1b2e9e089959a29a5ffc08a7d1 |
| 34-examples.html | 81ba297541b051e5f99b46e80ef38ff51ab0c67c9355c7ba30592d3776c7f81c |
| 35-property-testing.html | 8010e4cb701eb6841c5b8b159088f81f56454bc9eedde2323299a25e8a6d1381 |
| 36-grammar.html | bc8efe0893d5c73cdfa3f3a87ee6ef6aca7b531de0df189272472d24a7a94b22 |
| 37-appendix.html | b533631300e789165d340361096bbdf96ac27df784b5778e91cc845c32d31f9f |
| 38-appendix-gpu-billiards.html | 40d3d5376471d827c9705df2c3112b8119191e8f4f5f7ce152416158e4123f99 |
