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
| index.html | 1fad601ee29f30ddb63e96c53bd7c8009bec166341365cc1e3b71176bc6de58b |
| 01-introduction.html | 79b777b4aa6b288a576e5719d0e91914aaae5e7e5b91a88baf501c0823a585a2 |
| 02-tools.html | ad526926c6020e57b3794e9c1714bd4e93ea847bd655f5c3ab95ae38837fced8 |
| 03-lexical-structure.html | 7f31b99757caf07f7056eaaff2996fcd3f12c75aa5ddbf249a9b3aa1f2b0e1a5 |
| 04-data-types.html | 55425e235164b9187249de1294a245b4329c8f985b9029174ec015d7e82b56a7 |
| 05-variables.html | b2099f5ca2ad692741838f239980407f6a21de4aea1a98f84f7346deb16ad13e |
| 06-arrays.html | 1b30367bd5c526f2c5192c6432cd31d195ecdfb2dfdf7e6835d5805bb658b5f9 |
| 07-expressions.html | a7a130eb8b1b59af546dea9550f94419c435eb5f321a21de42dc807d6dcf4ad1 |
| 08-control-structures.html | 6e0c3b5e62beeea132729a970f7c451909b976368aed981435b637a89fe769ee |
| 09-functions.html | b266edbe141b3406f9096f66795c2c1fd935c1a3b8f17bfbe37e274c5311870f |
| 10-prompts.html | e2fd6219860312df115dcdd66b11eb78b74df86c38c3ac5340906a5f4c261481 |
| 11-classes-objects.html | 1d123c7618cdf7089dfd6963331e209afe8480996cc50c3e62588baeda38aa39 |
| 12-input-output.html | 5eb6ada0b2b3839f44efab3d7a2f050b05cadf8ffae58731154a234355a2eedc |
| 13-built-in-functions.html | 754c44111c9e0cdb4a0fdcf7e8eea5323955e4ece3d19122519eefb174917fbf |
| 14-graphs.html | f9f31adcf1653c0e04e1782125f2da1c226a1427751ec75b039f788a79cb4136 |
| 15-vectordb.html | 322f221dbf8cd1613e75bc44452aab76f2c05b007abb884b65b6b30f75500fc5 |
| 16-database.html | 8d79c98cdb26c93a25f662b9c00b274f6b5af789c0e4504113268b2a4b837b61 |
| 17-actors.html | 2cf24512b4f3a3181686fd0f2af6a775b647b2f12701efbc12e529ad482e886c |
| 18-agent-orchestration.html | 7449434b55812d1a563cf6d932ccdbeb5fb77d55434c5357338ccee847168c3d |
| 19-graph-memory.html | d7551edbe3d4a86b6bcc32dbd65e6073e5d213ee6d4e6b477bedf3c4a164c2e3 |
| 20-mcp-server.html | a266326c0a2bcb8f248627d3155a824e5603d6df07e43772c523af6b41e6ae94 |
| 21-acp.html | af2c8a1c74bd1eb424409b4eab66b842635b0b3cba214c6d99974bba1f6477b5 |
| 22-durable-workflows.html | ad2fd53435155a5daabec1d983612eac9467c7556c2d13304e14a75af5cda4b7 |
| 23-web-ui-hub.html | ae4afc35822c36febb9bc72f85f3c6286c64ad8156499f9a12fe594e7e23ab86 |
| 24-web-ui.html | 7f635c72199b930e858c1de74cfa840e1024f5559ade4a78e4ba13eb0cd2953d |
| 25-http-server-html-ui.html | 01beaf30813a9b31cff36ba276f67d1e2b78e2c6d0bcff772d32b97acebb06ee |
| 26-browser-javascript-backend.html | 2f01f4d28dbebd665dc66a4f12b16d4067678633331e5993ab4736c105943b6e |
| 27-rest-api.html | 874dec7a40433db346216040cca5491666e4e1ee4eb7a9f859eeb859814158b8 |
| 28-rest-web-client.html | cb6c87e916e91f2a846291ee89bd92b63da193a4ffaaff073edfa831eeb1849a |
| 29-full-stack-development.html | 405ad3a37a1b41fb0304461e01e0eab4486143a8c6c0a361532faec969e8fd3d |
| 30-dotnet-interop.html | 05851658a24a5443788fdb120089895233c7b4f942b4063567bb8e976561a50a |
| 31-device-integration.html | 4ab6b3a98173ff68c21be153e70f659e3812f8ae837a15294df9b803ee94f3e7 |
| 32-personal-assistant.html | 162ecfe8e778bbbae129ac78cc6660d6140b78812c6dce8fe92f32f7574185d8 |
| 33-examples.html | e8eac5394e93801aa7574e4f57970acc049d5bb13729c01a0f414a452e7eda4d |
| 34-property-testing.html | a3290236d61f398cd2becb4bb305d9a5e2233e02599dbd560aa5fa971a36957c |
| 35-grammar.html | 4fc2136782cdf149908b123e4f2632cca9263119779c02fa54d814baf42e47fc |
| 36-appendix.html | cc17a4d9acbf91d28b5ba8defff893f8c0fa9c946998c91aeaa67ea7259c6150 |
