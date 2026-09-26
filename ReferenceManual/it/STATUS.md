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
| index.html | df501e8450b2c3196daed8c7befe9beacebec15308d68c1b58d48949899c43ea |
| learn.html | c519ea7cf16835c0be7296c2be04ebb76ff88c4f80cc86d2583fba47bd6172a0 |
| 01-introduction.html | 53a60c0190bbc2f453b2f718f1b1afa2374270e095ab2dd16299627382bee63c |
| 02-tools.html | 639bd3e18d2eaf76574a8f6194027f47d276d6ebaf409f6d6a981f44061671c1 |
| 03-lexical-structure.html | 60afe22f0ef6263d8d947093d52f05d1d284f96c98364261963ef97d6c46f24d |
| 04-data-types.html | 3b169b216b27b89877b4c580b12baf3876858e7589059b1e4a5039466b7a34d0 |
| 05-variables.html | eb821a2a5bb51410eec988a74b7e87e66757b07b4b6a8a534a88b086369a231e |
| 06-arrays.html | 0c6f0760188ff20fa0c5591222ca0d1f54aa41bd96df1227bff61bd4c9e31a45 |
| 07-expressions.html | 2fd5adaf5ad364b7c2630a4c11c0d20d1350b0e6c027492e83b033de52473ab8 |
| 08-control-structures.html | 69e115b51c0a14e6bfeedfd1e6dcf9d8d1c12f6191a3f575d77ef26ff76400d0 |
| 09-functions.html | 2d86612bff923e38e1ba6d245fa0b44014701f0257411ff7167caf12b401cdaf |
| 10-prompts.html | 1d7367ce595353950a2de33a5c1253239389ddc555de4d9fa58d48e46a87febe |
| 11-classes-objects.html | 495fd7aea5522623f681016d508e277bf528ac98110d3eedfee0ee65f0df5bbd |
| 12-input-output.html | 9bf6809506045bac84bc9a0a0cddb03150303ad275c0694740fa84969a2f40dc |
| 13-built-in-functions.html | 52b97c9eed4c324af6da5a8f3584045f638724f9f8bf29de3aaa1f7936bc4a31 |
| 14-neural-nets.html | 01c9a9774772a99cd6d772a3b22cd720538c880258a010e42736914b75c3f4e7 |
| 15-graphs.html | 4ea05ab6423348683ccb2125eb54ce891114a2306ec4f23c7ab5bfb2232995af |
| 16-vectordb.html | 2fa68fa73a2f7cedf811ae5a352547eb327b0d512e79aaa9aafa60db071f210e |
| 17-database.html | a0333d0143401002812becbc640ca2d2fd7c419d7e5cdc96120a3bca54cfa15c |
| 18-actors.html | a7909d000aba255c06ad557bf450b138664c0926a778a1db80540356bba1868c |
| 19-agent-orchestration.html | 5946b205fd6e6aeb5186d0199ca959fcc19068c429fa4818394121d31d0fc58b |
| 20-graph-memory.html | 391ed2e2479333a5104b434e73a7beef0550ba0b8605b57028e684c85094411c |
| 21-mcp-server.html | b51b274b19821a0ec0deda2df34e4461ba3b859a367b540676bc538cbec1d17e |
| 22-acp.html | 61cdeb7efd94a3031c3261c2c0b8b108def32065893003cfa6ee5dd8ed353799 |
| 23-durable-workflows.html | 7cb668c0a4dce19a0a46d291c1ff7987bd6e69a230df8c8e5cdc9f9529a9095e |
| 24-agentic-runs.html | c17fb8e2524717677cd91f07212dd9500fc1331066ecee7f4d66c333371b63d3 |
| 25-web-ui-hub.html | 01924ce6a6bf487482887e84257c006525802f085e5c74bebd4d755722da5d20 |
| 26-web-ui.html | 30ec767c000917e834def1311bad87b0302e708ecc1505c74f5d64dad666b687 |
| 27-http-server-html-ui.html | 100816b83d19e29b7683a64e3ed3a45f49531e92bf833e5cc4dadf4065458def |
| 28-browser-javascript-backend.html | 71388882406048dc5435ad982182db0370e3596717dbb4a9764a49769747a832 |
| 29-rest-api.html | 529306f853f6179c8d50fa8f020f2692a0b98629ea174af4e7bcce0c278fd4c6 |
| 30-rest-web-client.html | 142a461d52b94923292d085e49b6becb2d8225207db8b2f35052da0b46791235 |
| 31-full-stack-development.html | ec0b3a0d8e6a66fd6585d4115b40128cef9ffcb6e59ee3931a87c2aef9886130 |
| 32-dotnet-interop.html | 598571dcb2266c11b40def9c2604c071a2a2d904016dc0041f44b621d8e98de9 |
| 33-device-integration.html | 4103b66cbca23060151831840454e93db22e4bdf03d0099adb563da6c227e5a7 |
| 34-personal-assistant.html | 96712980f2eb4251b644427590fc9fce1054fe72845d5e812187f83b1b843239 |
| 35-examples.html | f57cb2d518618775dbcaddab3e06ee0c5cbde73daf1d09e0abc7ecd6d4dee8b5 |
| 36-property-testing.html | 87138ecacea1da6fef2ff1ed06aafdbfecbcfc2db0000a55be0051a846e170a0 |
| 37-grammar.html | c1b3d7f97ebad8c65e8aed49cf5049465090801b87bc95f2fa6b9632b8840003 |
| 38-appendix.html | 3bc007c4d77750f3947566a337b8d79fa1244d600f45c67f4d89645a97f30613 |
| 39-appendix-gpu-billiards.html | d4f87bc6eacf37306e610216223373dfc550c12dc017fa64108b899fd75fe283 |
