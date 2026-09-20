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
| index.html | 0e0ddb00709aafa954ac34498557285cda9dc95ed6d88ac83d42363f1d91d602 |
| learn.html | e68c30151e0d98c406c88519261ba5c0906a0c934e6fef236ac6150ab835ec2a |
| 01-introduction.html | 82e2fd03e2a5e06c098027970dcbbfbb2893f256450430f82aaea50e24244ec3 |
| 02-tools.html | 7f01f7ea84f99ec68038193f0204e2c6d4346ccd8239310237387cc6248744b2 |
| 03-lexical-structure.html | aebc6c9d63a36f8148e8ed344fa1e0da584140316945ffccc5b718b8ac8c797c |
| 04-data-types.html | 7debd0bce6fe5eb81355dc54f598461d81b1d83ce4cae976ba3599e3356e53ff |
| 05-variables.html | 0e9610d36315039e98665b6c736c611d32caf59cf005ca966e7df899b4c5635b |
| 06-arrays.html | 5ece2609991a47083212373323dcd775bc5d50cfb675fa7f713c2fd3162729c2 |
| 07-expressions.html | 02c97d1693c127db673dc72324baa65915af743957fdbca5de02591ff70af7b5 |
| 08-control-structures.html | 5af289c2706cde62bb0819cded21fde0fb661e92a7f41aca5a2fb6c3564c96c1 |
| 09-functions.html | 9e4c753e0cc7d5c53d6a5b75fca5bce46e928cdbda88491922d5c00ebe22ea8f |
| 10-prompts.html | 47084678fa6e57ce52e4d62cffbfcca436b215093cb14194c4e47da862f87b1a |
| 11-classes-objects.html | 81b076ef7b592674014af8e820a125d18695e0d714d44f3562686f62cae25e77 |
| 12-input-output.html | a6f1b9aa8c67408934d74f0f39ed17b4d159ff0bccb2e1a140a7dd6286b4959d |
| 13-built-in-functions.html | 94578d80501db1f6c578114dec9c1685aaed3fe29e195094ab0d22887608b34d |
| 14-graphs.html | e39e1036613fea5ae7c5e2ede7a2e0ccd83317bf7b5d2edd48a6febe4882b605 |
| 15-vectordb.html | 228b586b162afd7623b90ddce3e811697e513ee5be54cc677f6409fba09e0ff4 |
| 16-database.html | 239abb72de3bcb4465ccce5b38e3a766a2a4d8c3413f89076a34102988beeedc |
| 17-actors.html | 5edc18f6dd4971d9b53fb44c4c9102dccf8085600ee1af02728b5468ed5a03a6 |
| 18-agent-orchestration.html | dd0933ac1fffa92f565a379120c5c14cac6451afeff746bcf9a8416245d3dc6c |
| 19-graph-memory.html | 5669977dc90f1dc0483d14aa95effe84dd3720adee03ead45507f4a1c34d1f45 |
| 20-mcp-server.html | e6f9d89308ccb3c63ba77652fa6cb7fb72bd9f3d57ba54dac15723f8c89f7f4c |
| 21-acp.html | b17dbca9a65fa0a9e7b3fd4fc17288a0ed3beacdc36184cba0a9bacb16992df0 |
| 22-durable-workflows.html | 793245645c721a8658154dd2f458ba11ee8cd44d8acda1eeb3c068235f31a532 |
| 23-agentic-runs.html | a8bddb365f26aab835425573f94831a12f019cb8b3cebd5b5b1952f1531b50e2 |
| 24-web-ui-hub.html | 2652bf7f1d5b1b3d466c0bbedfb8b933f1f81d64a1e5517a93c701650bc1ffc8 |
| 25-web-ui.html | eed62ad86ea8d31e02197dac44667ad21abfbcb1842f3dd0ffd6e69352d548c9 |
| 26-http-server-html-ui.html | 0ce2e4b32df0dceb6e951b1385764b202862588b1ad0779ae7a57fb84184a6eb |
| 27-browser-javascript-backend.html | a88866360a4f3513f13f5f5bbdccab79cf48efe3959497c67f89417c1785597e |
| 28-rest-api.html | 9a24dfe4e534d829d0521aeef44712c371f080e0386dfd71b43871adf5ae7896 |
| 29-rest-web-client.html | d9b944783dbe97bcaba04de71acae3cf00a9b60e6f90e1479b83f59318ebcad8 |
| 30-full-stack-development.html | a4dbe5728a112651c0df775b9cb187d521a538aeaff570d0fd5fc22b416bad3d |
| 31-dotnet-interop.html | a543ea37bc3355c0309331bc54353afd0c954a09e6c9297d63ae96d30893de32 |
| 32-device-integration.html | 8da849cfe71df37d2db88f19a9807cc7b4516593c576d6a145fae7501b0be811 |
| 33-personal-assistant.html | 7cfabe87bf14821551fce0804ff1846246b1b331bd7c1275089b582b265c3e9c |
| 34-examples.html | 3d3cfbd74f51766d9a11196d8f424d07f31322b52f2ed892a9d5c59af3616896 |
| 35-property-testing.html | e9e2eb0ab07911bd748e888405ea562d995c05c5f9b5aad02ba84db09056c671 |
| 36-grammar.html | 4eefba9e36662b2ca6e90a497ef9279a75c2d8574b704d6f47a7c844dbf701c9 |
| 37-appendix.html | f07c9a8ca08e4a7c63c79a49ba8843e17a5d0f047d343aadde360682b35796d9 |
| 38-appendix-gpu-billiards.html | e1f9b78ce423f866ce3d8eb309fae34e19f7606e7311b90bc54d28a26453d3ca |
