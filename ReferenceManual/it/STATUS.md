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
| index.html | 34b638cb69d58867a260b555a84837a404d9200c7ac9654ba7e3749d0b970513 |
| 01-introduction.html | f0fafee37f919a20c9a5c684f2572b908d7471444b7b20be13e2a31398b37f00 |
| 02-tools.html | c2de18af03008dc03f56a2f8dcc600a66fe7a64e21135fa82a3055aa6c3fdc31 |
| 03-lexical-structure.html | 4d80f4139d2bbec29a9ccc4cf2c65802b87cb067b54a0e41c55a62c57a4011bf |
| 04-data-types.html | dfa4f1ac41f69db4b39162851249c387eb9bdfdd68beab3357de1273e2c10788 |
| 05-variables.html | a30202298928dbac05888f10ae07c0ca3f0b01fcc301b779c06d7fbc77406740 |
| 06-arrays.html | 368a794fdff6c480dbef256145e56b80a0a16c5dace0a816119ae765ae43dc1d |
| 07-expressions.html | 2f59cbab65c0003a11a0ae51b1b846d033737bd44a0a2e11890e76dbadebecc9 |
| 08-control-structures.html | cc1c667b10d4a7b5960ee93103626de4fe545e50df91ed6e732abd4631cfee60 |
| 09-functions.html | ee96e817f925549616e36326ae963d33839acfe9c120699c79d79f28ce7f7392 |
| 10-prompts.html | 437b630aa79841552941a4df3f701f41de031c0a775aaaea8bdad83d8739090e |
| 11-classes-objects.html | 5348eedd5694358736c7300c2c98dbc53652d144441d44f7c13324a31d13dc19 |
| 12-input-output.html | 8291a454c7896bfa433201dca1753cbcec532ee91f9317afc5a29a60fb4aa61d |
| 13-built-in-functions.html | 14d5421792970c32fa6adcb89a721a76d124e4ccd7c356aa9d943f79b5476bdc |
| 14-graphs.html | 347a1016edadda55e027a00dccbaf624e285cb8b15e53b62110cc984ee16fc5c |
| 15-vectordb.html | a42804127392e2e324eea9be46931c757a506d3f2a6e315cdd62b6a70e81e32e |
| 16-database.html | b75b6cbfc68957c63bc2e9e5fe4cb8a0eca606942b179ad61663484b2604c360 |
| 17-actors.html | 62bbbefd25be20081aaeb5546709c24bd7812860ce7790c72987f0b24b953f59 |
| 18-agent-orchestration.html | 44829b80aac5632b5c6adf6ae58095e4bf26388a2b29e68d92c908ea34c51124 |
| 19-graph-memory.html | 17b92e79edb421d9f7586a71efc83f46080bbdb4271d5f43d7bedf5b98a5e81d |
| 20-mcp-server.html | 09805bdbee22f1ebf9612044fe03f51fb5f201a07b7db169336a0d38292e3fef |
| 21-acp.html | 4738d8380e660b60666c4b59280e6aeb2087eda984d09d27932c2c4a3fae877c |
| 22-durable-workflows.html | 5814673ef7aac47870cfba7feff381f21690f64edf0a79c349b84d4e9ea15c20 |
| 23-agentic-runs.html | bfa0c776d41f0e6c08d077db11f7b7b30a707c949f3d923f09927f416eb796aa |
| 24-web-ui-hub.html | 3aa92cd50e94e042221584f5bfd7f86403a13cd6ddd075e8e94c701632a91334 |
| 25-web-ui.html | 33bfd2e4f15d6295c5cd7acea13d9cfe8046280a49e9063079edc2f825f5d8e1 |
| 26-http-server-html-ui.html | 6d05fd8d511f6a5a05b6dace861492c3d5e47f22c471341035f60af439ab7d34 |
| 27-browser-javascript-backend.html | 1b320d4783145ca46a41ca5d1a72d0db44f4fb1ef13da694632f8c3e2a6fcb59 |
| 28-rest-api.html | 148c1bf1e6f43057c4460c99136342487c5df81b3657ede0ee348af4da3490ec |
| 29-rest-web-client.html | bc60b44697cca3c2fae711b46d602fd89cd93d5645b08dbd18be56107ca3aa05 |
| 30-full-stack-development.html | 04dcf3c3d71fb6f8bd87332fa60139ed39fd8b59cf2c2328254b2244d55553f0 |
| 31-dotnet-interop.html | b5c8a2cecd1b3b0fd1878f12a2b9982167fcd7307d157a316013c4d421352608 |
| 32-device-integration.html | be1b4d6c14d795af9910cebcad9ca2ef5498079b633fb1f542ab92fa2ac96edf |
| 33-personal-assistant.html | 114f37e895a46b4763edd1c7573ebbb1d412bd3c312d48efe229bd7769ffbf47 |
| 34-examples.html | 31320826b536f08f06c1c062ade4629773cad8c22936f9be93da6ef885213668 |
| 35-property-testing.html | 56db55d7d9717d783e9b7e9e3ac586f82d355998b6f27c2275b1e7868b23cae7 |
| 36-grammar.html | 8b1705a3f42ab85934e2e564588eac6c75dae08c919b6ce23daaf095d88cd461 |
| 37-appendix.html | 3d60ea6a51ad5c40edf336c6b99b67847070fa9eb17505c264d192eea621c956 |
| 38-appendix-gpu-billiards.html | 8e9ce6e4b3de2d80177f2ece209fe92bbb6d0722572a7da861b34d5196b04153 |
