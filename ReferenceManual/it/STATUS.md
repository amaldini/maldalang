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
| index.html | a0bc7db08c5c366d36e3b9889fef6142d0e7cd36aa1bf5c5d7f1a36ebf8dbdf9 |
| learn.html | d33158b5283703385f202d0d3ca18d6d168124b2318a0719327cb4bd550705a6 |
| 01-introduction.html | eecfd6010fbc8939d551b022afee1ede73177ac3a6bc441714b0460ba51012ba |
| 02-tools.html | db81dd02c70c3f8117f2e16f22e777f9f29d2c61318826b9e6fd6bb59e5b0fd8 |
| 03-lexical-structure.html | 62909f554dcc552cee5d621fa1a5ca6b6eb43056fcb9715874d96baf9b76e4f9 |
| 04-data-types.html | 37533f5be6f47ab840c8ecf15d7cabb93d9c0b0c938e9c36515797ed3765f71d |
| 05-variables.html | fed6bbe8b94c976a0465aceb644ec95a78ea2d6286196b44dac3877ed3de9e01 |
| 06-arrays.html | d731e8b2192dd1c7f524b65752d918e4e98121d0dff4c28b60878396ae55eb3e |
| 07-expressions.html | f825386a7bd2ecbd360b35a04be9f3a3193aeb430e8504d09f33f68d111c2a12 |
| 08-control-structures.html | 6a60d30c3ebf75d915f950fad66cd79007cac0b3a66d7edcd0e7c60d2dac9bd3 |
| 09-functions.html | 60f68e4fb986ce2ad9504c6b03c64461a14bf486c3383b36cdf1e095d6e29563 |
| 10-prompts.html | 04fb585f0f6a0214bd0c181baa30513268479852149a01e7d5d8e28b47247a13 |
| 11-classes-objects.html | 8dbaa56bf05e4249e98bb26d0fc9367ef50f630f7b60797c19e836ba86a3eced |
| 12-input-output.html | eb8fbd82da6829b8917711d58b9b3f1ef745f5aa82fdce990c30000493c5ab7b |
| 13-built-in-functions.html | 2405486274421573b3504266c62a799215881e115e641fe3d780b2bb4d91dbe9 |
| 14-neural-nets.html | 00d74c325639fd3a47604e1abcec6e6a6b5f39e74d404a5f7d79f3725134ddfb |
| 15-graphs.html | 178dfac8b25737e4c15ff9d402a1dd96b3846d4c52f2ec49d28182059b0a6a7b |
| 16-vectordb.html | 7a2a38d084d28bafa1ce983f8d885be859491ede3eb3405cc63b0a284d6a78ff |
| 17-database.html | a2a722c5b9e0abd18359a731715505f6c91f39eb9708a528046feca1e6971936 |
| 18-actors.html | bb3287e2977bc72f9f2d549a1a816361da44eea40e8fce2372c63201dead4519 |
| 19-agent-orchestration.html | c5f305806acbe3f006ebddfcb752ca0c8133cb188b0e428748b8707b33c646c0 |
| 20-graph-memory.html | 3266ea27171479589539fe4f20ede297022b5502250ad893a5a400eaba4be96e |
| 21-mcp-server.html | 5930dd0a6f52832369dd93e3478bed12c65a54dbaf3c71cceacac7a584df8173 |
| 22-acp.html | 0d62c9a70ae1f29d7c10db9c1ac00448dc0efcf4a9a250a7c99423274dde23c1 |
| 23-durable-workflows.html | 79fa6c68448d4cfced6b3b399092fb691f16494c03553d561b8fbff6a226c8b1 |
| 24-agentic-runs.html | 9fdfc39b4ba1ce2fabec02cd90d87f73bc557e57daa6469bf207a5cbfe2e0f58 |
| 25-web-ui-hub.html | b6fdbcaa5c541edf9c8ce204dbc91a37464487cddba82dc09c7404fbf63e882f |
| 26-web-ui.html | b9db7a5711d3a1f42f9b83afd7e0c3b0d10d2cb10de14b2f273b181bb219915d |
| 27-http-server-html-ui.html | 18f1e0a524b2ebdc259f4ca00d116d9838a06a29ed227d37de4c2913ac63a261 |
| 28-browser-javascript-backend.html | 99cfd08ed34c9fc36db5281a8f8d2c26617ae17226ce0260ce2efd81ad1adab6 |
| 29-rest-api.html | cc90903e2ace9e20297bc0536189d68da3f13b9da5f5344442907594c085ac0f |
| 30-rest-web-client.html | 8207ef7bdaa6c53ad9803ebc7ecc04debdd99f2054e27f0d4eb7f1fac67f981a |
| 31-full-stack-development.html | 4125da4b478198e43f1ad28055d5177972d0d36c2bb8f4f333c8ad9aed89e868 |
| 32-dotnet-interop.html | 3de96b7e1dc89b0e4693913139638480992fe33b1c0d57bd2a506de68c7b525b |
| 33-device-integration.html | aba2035801b32b83725aa3f4b9609e0d135cc019a9446e1004e40bc1e6351fdf |
| 34-personal-assistant.html | 6e4bfdeb93e312407b9b308359456a17974f6d3f68f89c0597db216903223746 |
| 35-examples.html | b4cf606d19d95edd5e6bba57bb0136d940c662fb27971c6e4426185e2f535025 |
| 36-property-testing.html | ca3e9b414d46506e7fa4ae72e6f2097d5c8423e5ff0d3c7602c5af11528edcf0 |
| 37-grammar.html | 964056eabeed26380ac4ba294ddb04def4f1dcfb92ced2e8cf6c13ff4e5fd617 |
| 38-appendix.html | 200e2a3ef584cf1549a032bd2ec75717ac7fec749192b219548bb51d9f777ec4 |
| 39-appendix-gpu-billiards.html | 75e8b4a65155807421dd4bd6e25a025bd98aacae76215314d8ff3fe136d39566 |
