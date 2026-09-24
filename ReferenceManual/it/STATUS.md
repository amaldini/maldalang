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
| index.html | fb9e237b6545622d299a235f5909b45e4c02e9ae5db8555f63a27edb7582e64d |
| learn.html | 804d5c8899231b81f73e80373ae6aef183121bd702eb4d8946eb3e09462bdd6f |
| 01-introduction.html | 4e3a324b864c307c36b4a623846277a6dcac4de815df7d9842fd5aac2dcced6f |
| 02-tools.html | f75ef765ba6b51aa2d0dbec741340280ca03e6290898b7132bedaa4400348087 |
| 03-lexical-structure.html | a529e57013fbd3669aabbc1308fe8f718f9712741504138d260bb833dbb1a8df |
| 04-data-types.html | b3d96ec844a70852eb9f2435e31eac47f9cbff204102856972d64e54b74c758f |
| 05-variables.html | d5f8f3b77c0253b46abde56a00f0f828ee5c6b1cffa1cc6a864a363aea36b144 |
| 06-arrays.html | acf3a46adde398c87dbf10f150f14e2f05e523328094d71c5213560644eaf570 |
| 07-expressions.html | 0bac545c9523396d8dcf9bcaf154120fd747bfc4dadc031d3914f2521b281935 |
| 08-control-structures.html | f05da5aae3f1140fca838f8f2be6c6ce3673b32e2fdbaf5f07aa3589feeef26d |
| 09-functions.html | d4d94b9a66c688352cf385ae5f464fc179d1c2f8f1ddeb4fce6680d28671cbb5 |
| 10-prompts.html | 17a7f8d0985dcc08032445417d073897d9dd0d87d06705f6e3e8b6ee2ab1a48f |
| 11-classes-objects.html | d41fbd6fac721997a9358d93d2bacaa3f78f09950277e179063bf0abeb4518df |
| 12-input-output.html | a24fec3ffaa9d71a34156b55bf0f974d10da0ccce01b6d9389fa3f1ddbfa2345 |
| 13-built-in-functions.html | 3a45edee9d46454767002ee7af81d9912cba680bfb8646a58d83131e51e54948 |
| 14-neural-nets.html | cd21f755e4c5002e7a7dc2dbb8e4e15c4b83da66f7aed0032dd0af35310cdc32 |
| 15-graphs.html | 964f7b8fd689e094bda183f18a98d2536ffba95ad556250c51f8e3f651f7a553 |
| 16-vectordb.html | 5800ca7cc124afc623e51fa0d7896f9c1b87ba9974fb4b9808b1db8558da5b93 |
| 17-database.html | 0b46373a0c73e92783812e258fbf924245e645dd0b3e211cf1b0ca40cf76804d |
| 18-actors.html | 91c0ea19cb7b8c9588874c0c0a54063fd92398a35015c73994c23a378be10761 |
| 19-agent-orchestration.html | 1f4902fe9798d1cb47a8db1f977f25eb8134b5581bab5edbae56855eb33aa581 |
| 20-graph-memory.html | 6656e2b9d71cd2210c97dc04f0fa64eeaa9fb834540de66f95a942551b762297 |
| 21-mcp-server.html | fd7628cc47a71548198e41617f472eb8c363fbcb372d21b773a6190413117eba |
| 22-acp.html | aa2f75605622290558f441c7f3b265ffce1efa562fe5458a841231a97e662b2a |
| 23-durable-workflows.html | f35b4bc48d18a11ae81f5ea4ac7b1596690ace2b0a56c452e745f9954b58fa19 |
| 24-agentic-runs.html | f98794f8731c7db88f786ab8a78c91a3362ca6a886098ecad4558892e4487804 |
| 25-web-ui-hub.html | a642ee43c4f873b421175c57e29421b3ce9ac42d4626f69b4f8e89ecd154e88a |
| 26-web-ui.html | a9f867977575db02d1878b62453f33615ce040f85015eabe14ae08316a2090e5 |
| 27-http-server-html-ui.html | 139687db44299c56209795d6a174889589cb554bb39556022ff5ecc2df306e92 |
| 28-browser-javascript-backend.html | 30b6523f499e3ae5833e27100671a9e8dcf7feda420d198af0223cba7028aadb |
| 29-rest-api.html | 95fec66cf3005a67e4fb573bb04650ec0d6f9fb2b3e350c5b4daec390604882e |
| 30-rest-web-client.html | 90d1213d04a98c013eb678371e816eec2bafb1066274f339356faea3d603a2ca |
| 31-full-stack-development.html | 87ec0d336e39f6d80a571eac67a8ba0c84e92d0f72642009ee027f9479ae76e6 |
| 32-dotnet-interop.html | 699e38adf952adca4baf4bea28eedf96eaaa0802bd68eb9ea2835ce1c98fd6de |
| 33-device-integration.html | f338c1162b88d909b2b7d0205062a5763afc5e04832453254051dbdfd5fefba4 |
| 34-personal-assistant.html | 18a00ebc1167d3893bf0f199c5eadc231d74657791b92ac092782a32f519bc35 |
| 35-examples.html | 2a464bbd1ddb6b35d1eac059c88cbc1669747e4df310b2e5508d006bbe5b5186 |
| 36-property-testing.html | 5fe19bbe304a87a6c0ed0f2eeccb9e421242c93cfd02c7794ce3d24551c54562 |
| 37-grammar.html | e2d38179225fef935abaf44f8a9df189a1e9d702a0b728bedcc9070d16397b4e |
| 38-appendix.html | 96fc698f2ff5d66c05fe3fdee8712d983b963fa6b03de583e160c7877aa83db1 |
| 39-appendix-gpu-billiards.html | 3b1c4b83bb11b0b72a201a65a94d56d47b4752bbc50a2dfec34d30e498b83e8c |
