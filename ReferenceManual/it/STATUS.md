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
| index.html | b81d2b817474c0cee98a44489575ecbadd6fc8472d779b4bf7f862aac25132dc |
| 01-introduction.html | 87ccd0fa384bf465c83361b8f9049e69590595cc1b1f4c0233ca782cefd37f32 |
| 02-tools.html | 38175d11773404480ead7ae92ea5c9cff2944483fbec7656fa6f8df372fcb686 |
| 03-lexical-structure.html | b059fd8a3d02ea52225e45cf6e1d2b469bdfe7d4b3378c88af0f80ed98b63f4d |
| 04-data-types.html | d4ba04ac912abf05011e6ccee89bfcff0b54143766ffc6556b6379618ba833d6 |
| 05-variables.html | 7bb29623757d5cd37f24fe8de5ee25d470efb76229494e2d123112ba87933c61 |
| 06-arrays.html | 61c7f94b244e43338f9a3c0b301b15aee716a5b957b2a7a402bdc1887ed9328b |
| 07-expressions.html | 7e3ed8c2e29f563e9bae8effa17fc6911909d72f39b9b3bf71d8201d8e7bad8e |
| 08-control-structures.html | b6d02467f6e95a6c5ad138fce304317fc63b3291e0da3fd5ff9773cb47ed6d36 |
| 09-functions.html | 0d8e1f43c3718761d6f3ddde1426206033f03871b6369fbb483fbd42cb6b4a43 |
| 10-prompts.html | dd9da9797a4e71e01621c5e6cce49b11bdc2eac124625ec073ae1fadf158eb71 |
| 11-classes-objects.html | aa71e4ecb1ea1cb8f1cdc84515df17782bed637dffd8706e759a2bee368870c6 |
| 12-input-output.html | 6e1e8d960090be62cba6fb9e69ec12fde943399d209fda6f3f26a8544a6e7e8b |
| 13-built-in-functions.html | 24fbde8ff6c6a12d897a601a90ff8387ec5ef6c3cbb277bbd0389c94eddbcdfd |
| 14-graphs.html | de955d840cca9d83d12680228745270240bf605280163f821b08ec92f357a84c |
| 15-vectordb.html | 845bb29ad968e201e57201d7658f7b19260689584a2bdf7d6393708914585875 |
| 16-database.html | 95eb496e9c3c37e3bd2b50d35340b7de85dc96c912bf834fe820ab8cc3bb3da2 |
| 17-actors.html | f7de4f51c04c0c3d4214191fe22b5096f2ccdaf15b405e348fe7ee962963e5e6 |
| 18-agent-orchestration.html | f1fdaeefd52c961463ad56e885db1c8b012c5276bdb4e2d5f2f85715c82a0943 |
| 19-graph-memory.html | 6ef6ffcfaf620ed4982e0c3271e09bfd95e63af75088935d4dffab515ea4fe40 |
| 20-mcp-server.html | a42bcd8d2129d52ecaa98b5dceee4050aa50a23a870a4c3e558bc13a7fd6c8eb |
| 21-acp.html | 153b71f433ac476e316bd1ae4e47f28101895dd1da6a79fa9aec8c685f27b562 |
| 22-durable-workflows.html | d776157f5ecd2397927f92d998413824d62c092db480b8aee908e8c472a4f391 |
| 23-web-ui-hub.html | f3e2e706c1df0fa9c307d7f32d5d03727a8d23afd7ec68277aab034eef0e3b88 |
| 24-web-ui.html | 39479582dea892dc58bf39704ba3f05bd03ccef3f2162a042db9250f96a4de78 |
| 25-http-server-html-ui.html | a6c653653fd4721925301ae8a49b17d67cf2f416fd04ae9fdcaabeaa1ed6f6a6 |
| 26-browser-javascript-backend.html | 951ee0bcb32e9ecded23f4b81fddbba60bd133eeed963e024a5388fe61344d6c |
| 27-rest-api.html | fcbc6cb372e8573e921b88e503669c99a2732f3f106af21548c33c6dd6132b08 |
| 28-rest-web-client.html | 514cc4f1fd45b33a6ce3137c0b185d2a9a4a2a6461dbcc5aff04d998ecc1d470 |
| 29-full-stack-development.html | 18609171f2a981ce23124f60b244281d14b0dc57ea5e968dc26d7d42d18500c8 |
| 30-dotnet-interop.html | 153336c25575742b48e44592600a10fe98f4065a2b6fc2396ba93b0965e8431d |
| 31-device-integration.html | 716cd495577049793cfb4d9bd1852673184ef88634aca19be30a12ace75d67e7 |
| 32-personal-assistant.html | 1e3e9ef1e7a8f358d2fe878aeb7e510ef7336e2d4ee2194c255d532ce5ee8cec |
| 33-examples.html | 22c97e09b31984268f2e5354d8f1d15290fd4949fa2391a7e521a515c5ac4b13 |
| 34-property-testing.html | af0b6cfd71a2f3d7280547ab487425a8d2c0a4268a4d131be3a726fbec716b1e |
| 35-grammar.html | 50f1c1d4f2ca0f6f539852ff7b12b17be435405ff95a5f338fa708f8770829a3 |
| 36-appendix.html | 685c5bd7e60edc77b7f45b0d9e70494bdfd2e973ad860b5de52b10ece4392ac1 |
