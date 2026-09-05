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
| index.html | 69e33b392bb86398cf9e32add8f703ee899cfbe233709709dedba2aa5dacf13f |
| 01-introduction.html | cc118c6c2faf4144660b3612e8cd077cd9cb32ef0957754fa152d6f895c362d8 |
| 02-tools.html | a0d7e35b89176bde4c5639687fc616a95f84a21a5566e1b178c1b7070258b17d |
| 03-lexical-structure.html | 5ca8837e7f4fa0d6e91f47a2670c5a00701292946f6cf35a48f631136af7d35a |
| 04-data-types.html | 9203f390afdddd3c11339765703d4767275e6a5bdf6829fbaae34b84401520ed |
| 05-variables.html | 221deec11b74bdcc7558ae10779ce6815c7eaaa0f04ca9916b1a45d81945c10a |
| 06-arrays.html | 0f4ba6b313eeb17404319d33594cb405f1f20b15c724e54bcc4ea043b6d18d5c |
| 07-expressions.html | 12726a63f59c4bddd91e3cb3ba4cd20cb396dce114bf02e3f06a961d64594b3b |
| 08-control-structures.html | 2b5e62d2f23cb5aa7f64e6090dda9072ef9ae8a54337a4996f12a30f0307744f |
| 09-functions.html | 6813b0e2c447a8e290dd7bdc0c7eaeb2a3fe4ed7445579c04beb2b6b518d9968 |
| 10-prompts.html | b96a89901c702318ebdd43319f395e4edc840eeed9b23d156c8847112d951562 |
| 11-classes-objects.html | 6c803ecad288146f7fecbcb6c180f806876a7b02a3f5bbf11b269ba6f5741fba |
| 12-input-output.html | 3bccaa1f6a4776e2690bff7dc3086d25871bf1cda1d1bcc32f99fc611ed6b97c |
| 13-built-in-functions.html | 5217667d1eecc99e1ebb89feb7ac4d68d87fa6cd896f8733e527277444bd554c |
| 14-graphs.html | e1d0bcbe38cfc57c5642a997f2e52373d3ba8cf784bf1520879cdc6b686b6080 |
| 15-vectordb.html | ed3ea55583b2d8b42f615f4da3a03fa969f56bd9a7bfc1709e78d0124c16f072 |
| 16-database.html | a47454e18071fc0d091f5dce024d98bd6a49797201555f867f934e04c63301a4 |
| 17-actors.html | 792d915e87e3170442707ee6ac741e77c9c88699491d364ee1fbe1656b50a381 |
| 18-agent-orchestration.html | cb7a04116e1a7467e89769bdf147cff238b016f15940269bffe94ee3b078e63c |
| 19-graph-memory.html | 2de060baccd20739a4ab262240474f663e7242576d4b908578379655486b7a6f |
| 20-mcp-server.html | 2090aec8d2aa2a27e706d64410d74dd4b0d3164cdc4d327f4dd2792237989460 |
| 21-acp.html | 6008cb499d3e7cb6747981e96516167f2a374aafe40add9c97e20d7d97c6823d |
| 22-durable-workflows.html | 591abc367d4e70d71ebad2229a26a3906ee63f5282762a696bca3464d870addb |
| 23-web-ui-hub.html | a5b2900d64f6d82e432c94f1b8a9a1264cba0e5db38e8713744ccb2d78ee6ca5 |
| 24-web-ui.html | e356e198bcf44c103bb7eb190f84768ddfd86f451f39d88265805c8e9028b1e0 |
| 25-http-server-html-ui.html | e1db1f78e58b3228e27678e41bf541bc95041a5fbd1db627099118756371a898 |
| 26-browser-javascript-backend.html | e9150e7bd4f4d6ed5dec6d5bc9ae625f3af2b60067823691e14ff1ef7a6a4c9b |
| 27-rest-api.html | 0e8db47a6d38774edd17aa129ccda36550b2c8826b999d6e59d27406c84dd516 |
| 28-rest-web-client.html | ccbc7c7db06373c0b812fa186fcd05cfe4ec7bb1a5a894c73b7af782ab286099 |
| 29-full-stack-development.html | b8bcc733218cc20fe14f60f78d0db93b8ce45524cee18fe980ab0c6ba754bb76 |
| 30-dotnet-interop.html | fbd81e0e4a05a421588cca3e0a9d3b1946a48bab135923376fdd4b08f2593d3b |
| 31-device-integration.html | f56bc49cdb55dc8b7353382129a63add2998983c15e02fc83f01e3516d8571e2 |
| 32-personal-assistant.html | 797aeb3fffe53efc9f914448470265043835433c0b91ea99a80f2ab9946d08fa |
| 33-examples.html | da5fc64a7f1d5a9722fe0831ee409f249047f35ce8e824e6f8d3e288ffd28363 |
| 34-property-testing.html | 15974eb96e6b9a7b051b7edb9a663241bade47adb2d9e6dac27dfc78c14cca6f |
| 35-grammar.html | f6bac292c05c03ba477b8cc0b3bb41ad702ce386acc70ce754414e03b7aa5077 |
| 36-appendix.html | 8b8dc88d928b3471fe6dacd6b103fe0351ae80134195b3402cdbfeebde017754 |
| 37-appendix-gpu-billiards.html | 610081738a2075ccbad97bbafc9ed5554902e7d0c67422621df96d81bc302860 |
