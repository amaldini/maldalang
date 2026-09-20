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
| index.html | 1377b55d7064bb09f26e41616a00f1b15ef47575d5f24182902f24d9df4478f2 |
| learn.html | 215b4038d3cd2326824d9a96e428426c7d522c55bd6a7fda99a02e41ddb6728d |
| 01-introduction.html | b9ad7c38af42968ad32ee2f8719f4fa83f1e8f38b524c21e9078c9707bb62d44 |
| 02-tools.html | 17e2878da1b5aada85f91af4bd7ae3128ecb7e90b497d94659341b7d19d69c18 |
| 03-lexical-structure.html | 0475a88dcd5b64f6d749ca9d8dbe219eba8bb47e9937629fbd2d25c43e3d0713 |
| 04-data-types.html | 66154ccafe6db4d2a963adbd83c5ccfe2b29121dfa8aeede05caa8fd75d6f0d6 |
| 05-variables.html | 858ff9471d8b44d50281236c4261fe10bf970eb0b271901bfc6b045b6e637795 |
| 06-arrays.html | fe38642d98f6e0d06b2034c951d98353193fefcd6faf14f5a708a8f10c6561b7 |
| 07-expressions.html | 1afbe303c4207d93b485f1a958acd045150150533115a3e8ce2691d937eed103 |
| 08-control-structures.html | 9cfb9d975946070b00885712ad04badf38b43e75b2e2766f81a4818fe49e8338 |
| 09-functions.html | 0b7e756eb24fa3a0d6b42433561945c1cee2c22afe4577c6eba53c8a3173d3bd |
| 10-prompts.html | 5619079c6dd2c561dd9bc4de5f002ee0504d474362db900aae027f45be8d59bd |
| 11-classes-objects.html | 6b77f9ea8a80047b3d5e898f8e26741c894b1fa6f74f8597db6c8ed16e01270d |
| 12-input-output.html | ec83e79dd6e61966e4aef27afbf32d0f0a3602da839702bf0d22744391c286a2 |
| 13-built-in-functions.html | 9d2c90f20b59137a8b931eac3dee1c26413a0f9d1fa63644b9c2acc1672b4d49 |
| 14-graphs.html | b71fb9b8d1b88200e69673575ee303939238db8cc07597573684a2fd1dceabf0 |
| 15-vectordb.html | 6d50e22e39cdb2bc186dd5896603ea3bb55110c887b7b200c5cbb9134c849c3f |
| 16-database.html | cedf0a9ca6f84a8eef79dd7e0d0d914bec1ad882c754e58041cc24ce85d44a82 |
| 17-actors.html | d5a6bd29a492404fe6b990e67a967701afeb46f6094d36ef6d1e7145be9d5819 |
| 18-agent-orchestration.html | 57b010f6a60c49fa8f842e8148be690d4e85a3a2d049a91261229135f26080e0 |
| 19-graph-memory.html | 5f097283fb6e4f945cffb7023470d2621f92b205d8e7fd1ab828401e4cfaf591 |
| 20-mcp-server.html | afe8c27eaacd20360d2aa718f2fc23a5309e17239d2aaa76472117e1ffb010df |
| 21-acp.html | 52f58db89eae3dd1a7582b52cff5c2ec672c6d8ca2fb471a8f039b5802a2f260 |
| 22-durable-workflows.html | 617d6b7ac2574029da04beb2fcda3b0863e7b26b1ac5a0e2c24415b6196a11f8 |
| 23-agentic-runs.html | 2c1e8090d8ce2eecc92af839c53ea1b7bd3878a4206ba8ba01b67de7c8d049e7 |
| 24-web-ui-hub.html | 684d06793df41c2979b39b3935c06617a8523377ba939718c6ed2ab61d42f7d4 |
| 25-web-ui.html | 52f63d7a8ce872cd3fff5d2968610cffa3e2b72f914ff44498f6c7c5f802d687 |
| 26-http-server-html-ui.html | 36f51311bbef1b548f2bf509ba0e1d3c3d0d30bc3ae1f7d5b7fdaa258c318c75 |
| 27-browser-javascript-backend.html | 6f085635bfda5e44cee49401f2af22ec2d737cf76f710f6a8db37668f3b066f9 |
| 28-rest-api.html | 05ea0895c8916ca3a28ab92b46a4710c54cce813a04d3a0444d3a52a954305c3 |
| 29-rest-web-client.html | 0fb1f7dd02a1143607e6505dcafb662ec7eee2e4158592ec82939cbf509196c3 |
| 30-full-stack-development.html | 6e39d8643d41c9ad0e705328a16a15d2d2d1f1af95fa4073ffb5e881c6d95fd5 |
| 31-dotnet-interop.html | 5639e8b6e34a3a2bde0a89c69962a8033481186ae20e8212876b0ce3aa86b164 |
| 32-device-integration.html | 31eef5b5aad42b61bab27aa0a88cad4f92aecf27b3fb9d37fbc154364774beb0 |
| 33-personal-assistant.html | f972ef1e93b3a4af419c7bf713906a892d8ffb322495f0c191d5500d9275a082 |
| 34-examples.html | e7796b41df5ff958623da9837d78a404d8dbf2dd1a7a0baf69fb607200ab9df3 |
| 35-property-testing.html | bc7cd4bf46ccf3435fb4f0fb89e4a4c12651bc60892304c26bee25f8958543f3 |
| 36-grammar.html | 234cda3f75d97ad4919672a0465c265e02625e262f3ab9789771e651eb21a8bf |
| 37-appendix.html | ed843bdb5b2e42381da1fdca7a7cb72fba636a7797195185ab1aee789d5b8258 |
| 38-appendix-gpu-billiards.html | 8137c74021b87a356e928d97563b543e1cb7b8ca43ca45eedf8e7d629cd54094 |
