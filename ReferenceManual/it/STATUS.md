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
| index.html | 8d06b57379d56f94e7af64ff757602205567cacdad83eb5bce916e26ae00b3b9 |
| learn.html | 735e272e50dc204eaa075dc2748200d033464d5264ae05332a4d44d1dc35cf18 |
| 01-introduction.html | b57aa66589b04b30e1c8f1dd0263dd7b8d5ef121ff8f8e6c9627573c06238710 |
| 02-tools.html | c61d4805e136e9f8489ca7643a2ac146f5d78d7b06fecd86c3359c559cdc2b5d |
| 03-lexical-structure.html | aaafd83188115e0c515a73cec2dae0d3eb1d5fd4e0c1ce9bde4c24808c783502 |
| 04-data-types.html | 0948291febdafd7f2e3a11417941bfca94b708217f3e0b6becf313c436c70f18 |
| 05-variables.html | bf0c221431d62f6f30a1fee14a3728edc21d1f0d661a3006b0047c36bbb53cf2 |
| 06-arrays.html | e656fcf7d8058696f7b8f4a62f1e2fa4de108bd38b2ce8e0417019fbb8393002 |
| 07-expressions.html | 2cd35997cf1c0ea2ce5a2ee4a6ae17614d8223193b45add4e54414f963d85072 |
| 08-control-structures.html | e86aca263a7237a6bad0993b392a5b499d22198219a3d7cda4817437bb7cf00d |
| 09-functions.html | b351d1ee039970680946847090312b99454a3979aa1ab6fec1736849c4e65d4d |
| 10-prompts.html | b3a501878b0b9f2e7ed03574cb8847b67246b014c1761ab8f4e31660a65befe3 |
| 11-classes-objects.html | 37bc62325021d1b4c5f5cdab67ca22288e83142d289b67633a73c592eef35776 |
| 12-input-output.html | ae7c37197d9bc28dbf2cc694c48a7f26c130722499d30e2be762516f1bae4754 |
| 13-built-in-functions.html | 190c11c3f477bc32f50306139a167f2cc7ba9eb201791938d0dde71506583456 |
| 14-neural-nets.html | 0cc7fdb4723b950d3e878ed94fadf467e25f70327683843cad2734676ec46788 |
| 15-graphs.html | 606b4076eaf6dcc72edec11a931bd8773801c9f0e097584ee27ae03a9f7b11b0 |
| 16-vectordb.html | 5d286480aa943af0e100aab798336d98d68577033e6a8fcaf5c41a6a358bae29 |
| 17-database.html | ffd93c0f2f3043a1390885fc1958cf17714b243efa388fdfe3b79bd456523d8f |
| 18-actors.html | 8d4590b2335ec98446288033b04ff39349b8fda9cc5368a7e9ac57a1d9530be9 |
| 19-agent-orchestration.html | 87c58a590e3e399a549900892923f29f1dc2e57e8def9dfd85a67e90338992c7 |
| 20-graph-memory.html | da7f3d92365f38fefef1877d9d4e70b269400b37509929b76b39f2c0c9e716ad |
| 21-mcp-server.html | fc3b34cf2b75d15735be03820fb71d928dd4e7e80c0677b77b1aa2ea23589217 |
| 22-acp.html | 5819f2d8a40a4ee0234c54426cce7b25a13556ba0f6b79a2c7d6c1a954f9999c |
| 23-durable-workflows.html | bf7df2554ca4832a26cbbd5f1e3a1ce75156c6243201126b89d1e25b7431f775 |
| 24-agentic-runs.html | a44e7eb70ec0b0ecdb0a0a5b592294d7e67ac0817457cee997819a446ddd8962 |
| 25-web-ui-hub.html | e8a6c867ef4d56c71f11e81e356aee9c620e74ac0914b23b4fd1fc78e270c1b8 |
| 26-web-ui.html | 147280bfb0369a44a469ba3e9b716e89c289de1358bd9db5ba74e1c01b51af36 |
| 27-http-server-html-ui.html | 0f3f5a8218cf6f96c29987d78821e9d35909632d732fedbcd225bb9a0c69c842 |
| 28-browser-javascript-backend.html | 47387f6ce8606e8a8e7cdf1151bfadbc09b58a11474e8a75785f9cd21e84a191 |
| 29-rest-api.html | c2a827cd6f320f5dc332126f6e44233955bc0d6856d4f49f84c3730cb0ce838f |
| 30-rest-web-client.html | 0612bbb793958e839c60863564448f45fd5a59b71c0b0d450b76a4f3506a2495 |
| 31-full-stack-development.html | c878198f691f951be9618678ada78ac74f88232b18a7b6f62f923ddf3efa439f |
| 32-dotnet-interop.html | e9c256d1f8da25428a42180ef11a28c8ac2c7be43bd90a5154aa8e04570832b2 |
| 33-device-integration.html | 24658f62fe124175aacdbf8e989a1622a2e05b8811d920bf2b9f3fde0ee6b7aa |
| 34-personal-assistant.html | f352156fe0219cb8f0817412c1b369dd0675ae32bd3588dadf799e52c1c702fe |
| 35-examples.html | 93c3db2938863bdb28d7c9d71860ae652aca089dce92a2d147bd70a37165a792 |
| 36-property-testing.html | db596cdf30f4fc2f97c7a2eff7709e17ee20be2865a37bacde902de4c7521daf |
| 37-grammar.html | 579eaf7b716d1f06056a1e4461114652d81d4ad8de0ecd236c2c5ac4aab2939f |
| 38-appendix.html | 9c71b3a077d7b41b138e8ee208c4ea1656e3d519b3d62c028e1afa9d600a8c4b |
| 39-appendix-gpu-billiards.html | a0f5dc0c273f0277a3ab5c8e44b3765e25bdaa18620cf5b7bf6d453eee04e826 |
