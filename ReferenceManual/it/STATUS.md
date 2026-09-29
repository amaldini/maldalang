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
| index.html | e3d8ef40b01781f83b8f0068c15e5cf25cfef65df0aa0f7e1e0fd35d71cdb869 |
| learn.html | e43456de4e01b3b814a1be01de4cbff1af4a0ef04a41d0580c232b9fc69af582 |
| 01-introduction.html | ac1f1648bd011e07232c685d0f7a697d5be7227e3b0970d385c924b9ef9ab22a |
| 02-tools.html | fe5a17b9e04783427aa2808a0499d7dd8847929a200304a5566e22658bcdcf8d |
| 03-lexical-structure.html | d065ff823a9324453acfec1e4648f1a91ad608c12877dcafcab980f376b8d913 |
| 04-data-types.html | bcfb53453b6cfc448892add620f81e3bce25ddb5eeb113a77f170cf9383c478e |
| 05-variables.html | 83abc15009658e2a01710b8b66f53068845f50d4280b6dbcc6136741b5f9ecb9 |
| 06-arrays.html | 26ea8596a85fd27828904b1ce82c1790e8e76f35e27559c25b836b08e8ae0f4f |
| 07-expressions.html | 22d81f4ad371d76f0805f452787f6d8ef1032900d9f33837205a4872499d3d64 |
| 08-control-structures.html | 298fdbbaba49f46fdeecf34e37b14a4bd6fd9534a2aa513e03e2be808ef0d43a |
| 09-functions.html | 9cfdc375107becaccca9ac8a7c8e33cf1ba5e43e3372f4c83d3891cc6d457fd8 |
| 10-prompts.html | 3cd18968333e7b8df3b37432e36cb37978da45720c9a1d6032919aca5a560994 |
| 11-classes-objects.html | f8e74ccd5c75fd463525901224c4d358b1e74aaf234c6e9db99aa2d4c82dbec1 |
| 12-input-output.html | a6fced16577bfc01bc8c5054333b4a62aed702cf92918262309070258b21797a |
| 13-built-in-functions.html | 6e3169bf291c3b2b7a02eb2d83b1ae68e71084de8a4b3387b92167d9df3b016b |
| 14-neural-nets.html | 22182b15bbf6b03088b1c7d11a6fdbd437cace2b597ee1a447b5ade8c4e3d18f |
| 15-graphs.html | c80662570ef9aaee42f9f4a1fdbc8e103a22d38991fe611fde49d3a64c5cb71a |
| 16-vectordb.html | 20fc2e7f9133f6ab51e761f5c257762718267659e9e4ef1b442fe566bdc4163e |
| 17-database.html | a0dbbaffa2e7d4e748682a492a1116878b0022a6d7490662dbc9bcfdf528ccc7 |
| 18-actors.html | 1153a833064fdf978134f4cc2d2bba67d0811d2bf4f2b2c25ebf5b9f21a33800 |
| 19-agent-orchestration.html | f41b07ec5987aee1e603faa4cd888cb5b88bd131dabd42ee800fa6ca9931a48c |
| 20-graph-memory.html | e24eafa0c1b660672f4a86de1e2696b63661432f68d59bc400560ec99b68b5b8 |
| 21-mcp-server.html | ab32ac8dc1aa85426dadf58030eba8a6a8a8b0d5f59d93464d6d1e58ae99524c |
| 22-acp.html | bcc620d417d3392eb7aca562feff08d57f40026e4c12343b9c93c2baabefe1ac |
| 23-durable-workflows.html | 1c9a894a39b623234b6387a4f99e13a9b35cf4ddeaf39058d984d126d9d2b690 |
| 24-agentic-runs.html | 30c26a7edbec7d1706ca98da3a8235b57b699072eaf2e45f1a7a73dae941956a |
| 25-web-ui-hub.html | 09cbe3d3c15b43a9d6b511f5de6c9bb436b16771b95e17379129322130606ae9 |
| 26-web-ui.html | 590d3de65531a2ab0e301f7b20da1d93dbb071d64fb3081609d8c918b4fc90a1 |
| 27-http-server-html-ui.html | d8893d92e8e9009a46d654f1c8400c5485d8a14e1e16755162f143b43bece13a |
| 28-browser-javascript-backend.html | 67376f581e522de148096e488530fe8b9013a8b6407cf2d315db944a1de14525 |
| 29-rest-api.html | f40219f8086fe22e0ef52f4e1deee05e1c38b08d04b9b7a1c74fa84280bbbffa |
| 30-rest-web-client.html | 52c26d93a8d000910f4f4018aed5879b3486ee21131277d24fcca2441498c3fb |
| 31-full-stack-development.html | 0c657408a039e79ab5f4ea8ed8668aa93cc8ebec87ca0612161ca4923b1ad5a1 |
| 32-dotnet-interop.html | 403b6e8587bf1a58a9774d5b0cec8f39c8acd22bc46da9980888216c65f3d858 |
| 33-device-integration.html | 064b1d191a1056f95e7a947e99ceeaa9285ce2311babae75db31780c3718da8e |
| 34-personal-assistant.html | 4269aa203da3c562da4cf15a996298ca841d720ffa9d6dfc71d6d121a0733f55 |
| 35-examples.html | c875eeea6a4483c3087e6468e7c7aa565a219b240e3fd8ed9110e189ee89fabe |
| 36-property-testing.html | 36cc189fc71d030c418ca0b492eef19bcb59fe0ce7e4a258adde56ad3a407205 |
| 37-grammar.html | 5899aad2162eb01d88ea67ed74c1ae58c3267edbc8a2f49dd0a6b3dd936988da |
| 38-appendix.html | 93be82fb5ccf03079ea16238d3f75f8d9fa92a5dc48c5c8939e32a5acfe86f40 |
| 39-appendix-gpu-billiards.html | 6210607d290b2576900b704c9d0fce2a19c0788b56429ec4da28a636e0191699 |
