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
| index.html | 5880795ca2f5b387e01375ecd45f873508c5fb4d2bd76c3e44a764723801cd2c |
| learn.html | 8c1e186629f9eeec016d21ddf58ab649c2c4dedb24a6335b0b0888e9eef67390 |
| 01-introduction.html | 2deaaefebca96c9e68c7a2ff65189fe57c05e546bc664ef686f772e9f6ecb08f |
| 02-tools.html | cfcd3431638ab6d4e0b4ba76232f7516caaf81abb6d4bf71c96939c93db49df0 |
| 03-lexical-structure.html | 4147a94fb4bc4de4026196404d4bcdff90259416d687d16623994d43a8fba0c2 |
| 04-data-types.html | 89b6641aeea3fc3fa6739502543e727aa250a816fb7ba38af01dfd16abdafc8e |
| 05-variables.html | 8b189bfdbfbb81148e23f599f255601c75225e6aa024804a583a7e7bafafd0e9 |
| 06-arrays.html | 8e3cddc9d4c905a93fe98c916b1c3a136f81030a58295cd54d2d732eee2d90d1 |
| 07-expressions.html | b3f4f1ef1dc942c22ff1f00bc43c51fee9b5c1e40649792eaee9925c660fe7bf |
| 08-control-structures.html | b24aeacb3c63195ea4f9b65e4558ea04f35aac720c4e973e4d7dbefde71fe73c |
| 09-functions.html | e4a962e0a53bdc0995b117e98c06f1a6ae95c4aef23b36823d76e6ac9d6d50f6 |
| 10-prompts.html | be6f8e558d3e938cda245a2b771496dfb1f6777bb069a4ba1984aed475784ec0 |
| 11-classes-objects.html | a07bea337f2a23ec62addb95b168ec542b16051c6a1fdf0ae2b685c0364bf4b2 |
| 12-input-output.html | 28a812ed215b66a68a7e1eb8d6c8f1b71ee635c1228870cbda6a470bc670107f |
| 13-built-in-functions.html | 4fb4252364e49ec2d590512e50b47740fc27de40d95220e20246cec715c863bb |
| 14-neural-nets.html | a11c41454f23dd484ef3499a7d39537e129e00ef1f41828b19a0fd58e1df316a |
| 15-graphs.html | 3b606b26636832a507dd55cd13392eec5ef1e691dc1f63848f25d1c8659372b0 |
| 16-vectordb.html | 245cf535ac8dc97e7a75ee0ea8f34916a15b8bdb937c9bfefdd4922fb2fd1b78 |
| 17-database.html | 34952f75940c7662f313595e140c04b2865f61e667d6c93b78a383846f3eef3c |
| 18-actors.html | 49e4594de101ee8ec485f4fea37831a26378128cc4767993da30c2f1253571c8 |
| 19-agent-orchestration.html | c0a17d0b8129ba83f35ed13f4aaa3dc6fa29f573dc21ae187168fdddc2312c6c |
| 20-graph-memory.html | c7c285bb4396ca070390eed3022d19017a8f39cdaa8de731b3af7d70398bbe5f |
| 21-mcp-server.html | a95c6d293d255b61d91d6aa4d241d5be23e87559a0b82919402365c7ce640cfe |
| 22-acp.html | 9bda5a05add1ed40329045e2824f08cca267522943bf8c8f69f55841c11ead65 |
| 23-durable-workflows.html | b44751ac5a4f620b3b7f2ac85e74054823bf87d679278d339392cfd578598fa5 |
| 24-agentic-runs.html | ef0e07d992f631d54ff2364c48bbb556eef2e65c87276125ae84087e1927a808 |
| 25-web-ui-hub.html | 945d225ec4ec1d081bd193f8d0a5708cb92ba2e55a5056345c5757968491eae5 |
| 26-web-ui.html | 9a946117ff7beb469bc06ae4b37902c089c6b9a8892e9e6ae5f22d98753539c8 |
| 27-http-server-html-ui.html | 4aece36b641355d53ad3b4d45b9aea1c943fd209f5b47c4d4db75d04a3b9a451 |
| 28-browser-javascript-backend.html | 688ee360bca85197dd6cc02aead8a94207e2d56dcc211fb53de17f589a8cd672 |
| 29-rest-api.html | 94c04160dd0f3cec3d148ebdc8494e8335b2d1143f3800bf43779f7b4fe4e408 |
| 30-rest-web-client.html | 672a2f0582971700277e32e5fe5686a11f5629a832cc8fde5d02c51081f79730 |
| 31-full-stack-development.html | 7862bd77949cd7b8278287af68c0fc4e027142d10f3c5dcf4e20cbac575cfcb1 |
| 32-dotnet-interop.html | 026605e439410492d043c6a8fd423f119e275ddb95e9c7e8d22920db964028bc |
| 33-device-integration.html | a8fdea146f889965591feef15029d8324270a9fb97c390e06397d21b53534974 |
| 34-personal-assistant.html | fa0562332ff8d0da4e477dd8eb1989dd0a954bf2e689d7346e3c4b3ab82e586e |
| 35-examples.html | ceb5a0b796a5607c801eb62fe0a98943717c802728f6b02e87e5c43e21461049 |
| 36-property-testing.html | 2f41cd464753c50f0a466bba645b352b4bbead4b74966fe58edde272dfb94e39 |
| 37-grammar.html | c8c0d7721d7215d58d479c492a5f1f4b2aff025db56aec3f56f2468c94860c61 |
| 38-appendix.html | 7f7b07026266129d5c08d99518af35a15320c250cdf561d170a4e9bf0010c611 |
| 39-appendix-gpu-billiards.html | 7b3210870f074f1803f832e6081d2172b74aa3c6ab03933e2066c297400b21ca |
