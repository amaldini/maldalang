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
| index.html | 68a25789480d0b9f334bcb350716a5acee0f984578c18bf16f83a2d70389a01f |
| learn.html | 252fcc7fad9c0417d83ddac6936a3b0eb3d21a64fc20b23a6d9e9407f6e7692a |
| 01-introduction.html | 9cb2932448ed9873536d3692d6a81785c407887248a6080502745c55cb854334 |
| 02-tools.html | 3150afb17b1e0b0e51f7f738367997128a02c49803728e121ee4da22035129db |
| 03-lexical-structure.html | cf8fa4df625c644119ed984f8d5e3cc5ee51d5fb37ef340f4b263485d2444700 |
| 04-data-types.html | 27f6c310a8a952ca95f8f113d1c78fa07e32a5a3d40be29cef948bad4a2e049c |
| 05-variables.html | d542e1861f71db518399b344e3d1b4dedc423b4d286600b1d4bc7fa4cc496dba |
| 06-arrays.html | fd0d8e412fab11df04c07f08ef60068f9aef79de7185ed5d4764b7b8a7c2d746 |
| 07-expressions.html | 1e2db1266f105a476c475abe627cc8271356095846bf2820d2c6b76a9dd02004 |
| 08-control-structures.html | 97373942a686edb8f7b885cd14c0f70be1f0fd7d1eacfdfafca50991cdee6f4a |
| 09-functions.html | c66e1a306e59ab46c0bc10f27ee60d5a06599b7806005d3ffafbba58c996ccaf |
| 10-prompts.html | fc8cd84b48c0bf07a09525b9cafc41ff9eb88efd38c00bea40bba060c839a92c |
| 11-classes-objects.html | 7f43a59438b64545b5db4620bf8c7a23b734f70392e84a0afc7e7aff22684f3b |
| 12-input-output.html | 2be48d081640810fc0b801aa0d8380f0ebeaf0bb1a378f8c391a94c03030038f |
| 13-built-in-functions.html | 64b6bb3d893e43468e16c9a2b51996e65f01697d6ee0085ec489ed06562f5010 |
| 14-neural-nets.html | 237ba347cb8db92bdb7a26390cc050d6645a3dc637a2343975163d8ec6861f7e |
| 15-graphs.html | 930ddb199559ee99b2a58c3cd4a3f6b82f744dde5243f78a685c45b7a20360f2 |
| 16-vectordb.html | 0d65735b3ed152161a72d736a189cfe086c2654ebf5a140ee23a508fdd0d1969 |
| 17-database.html | d32d75ed19f776f63ea3aa3549b76cd5bc7c1187c9de8ac05868356a30cf09de |
| 18-actors.html | 497a8ad5a499ea814c3f1c39b28607e8f8a22664448cb6412a261fd01e0b8daa |
| 19-agent-orchestration.html | 3806145c1cc8350b1264ea621244a52e5bf386ae26789020b33223210b81faf2 |
| 20-graph-memory.html | 7ff9f2344fd3407a3f703ca81d8ec2291b124c92e65df5e163e1e59d25f4d130 |
| 21-mcp-server.html | 2ee62f9df917305b62491aa65c3045aa25355042315c4436bb2fd9d0cc791d79 |
| 22-acp.html | dd31667194227544a3d264c95a533fd21c61960376614b365f0e402c0a5250ab |
| 23-durable-workflows.html | 05618abb884481308032c7e100e0015d3d9c6dbd2f0d78589ebda5e579e957c7 |
| 24-agentic-runs.html | 3bef461736acbb204b82d2fba6d6dd68ffeb319f98e17fe8ebbfa708782be4cb |
| 25-web-ui-hub.html | 61a06058128afabf16cce7e09a1b8b1fdb5f3f1d70be1ea6e65c7b4cd0520529 |
| 26-web-ui.html | 7ed651ab45c98e94f5def3a05f0e79eb537fbd0cf5255c64a9124c83ee202425 |
| 27-http-server-html-ui.html | 86af89f610fe7038841c67512afc7ce43a3b2e1530d1b4157c43d63a2820c05c |
| 28-browser-javascript-backend.html | ee2195a54d0d9097c0965cfd78f96a32c10c690f3339ae9bd3bef117ed5f3551 |
| 29-rest-api.html | 6921513d7979c30d701460be3fd50ff1c5aed0596d67b160a40475ea6184ce48 |
| 30-rest-web-client.html | 946903c2613c9873385a22cc29654e90ef29766e29dd6e646269adda6c20edc7 |
| 31-full-stack-development.html | af776b2d8f61e59d59aca5bb2fb4a44bd31b53c443e0568c5e4a98b7bf612701 |
| 32-dotnet-interop.html | 4cb43097584cfcdee7c193b43ed1988a45fcdb918b33b1514a6c52d0d44bdab9 |
| 33-device-integration.html | 5caee9a1b440f17a357933469341b909169378d5cc00c49cddc01cf93bfd6f8f |
| 34-personal-assistant.html | 5b0a1a45f19edbf237be6005e67a1431ccbfba5856f27c3b07dd25bafcd12e11 |
| 35-examples.html | 0396b80b6a312596b33feec94047fe223d2a09cf617bb9be22de3605bc536ff5 |
| 36-property-testing.html | dad53556adbbe0bc479912e512dc0d9c6bcb42d2cf5d10968a687900657fc0d9 |
| 37-grammar.html | 5e4ae6b9ba5cb689fc174bae98fd7675c76cf35194676b76776ad9d1041b1348 |
| 38-appendix.html | 1954a3cf204fbf2ee7fc2391bdc483a2ee5333094c08a74f795e2dc00b301567 |
| 39-appendix-gpu-billiards.html | 979b52cd49db3abd0f66f99b74424a9cc6cde768d7f9ee345edc35dee8c324c1 |
