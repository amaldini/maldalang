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
| index.html | 2cc7a7e24bd82e1cabaedabf658756620ec38e64be1ae1dfa7db5de0b5729b41 |
| 01-introduction.html | 75b78b39dbbea011435177571f3ea98c5741c116fab78a704c00ed52ad3ad35d |
| 02-tools.html | 5f8d541394a120351bb4fce44266be04d02866003b5a60cd0324211d52f23878 |
| 03-lexical-structure.html | 5bbd491ac7b94877e5698631914e387b8d6b8debcb2a30e5725791b22f14e772 |
| 04-data-types.html | 29be878cf578321eafe4ff91015a16ab6982ce10f8dc077641d3cf48dfe35075 |
| 05-variables.html | bf149cb28354c5a9be7988fbd6ff6c4e86ed660a8094331a00b11535bb9fa999 |
| 06-arrays.html | 028d9b0e82cda85bb7c7f1daf126672f7caec06266d33595992a3bcb488fea02 |
| 07-expressions.html | c8782366b3b44771478d85c3728437d7731bb41d7599e04e3b40e2bfbd1b75db |
| 08-control-structures.html | beffd13365cdefc65437ac76b7c2b3c0f6b47f676fbd8d336e8c7116949ebd82 |
| 09-functions.html | 828968658924370eb13fde07c30ffaba6e294ccad9f2d583299394f614bd6953 |
| 10-prompts.html | f5d020e8b806d7b4983cecab46c8737e0b1ee0fde80d9930263c8e3b0497ca3c |
| 11-classes-objects.html | 6a6a59ea87153f6e0ca22935eedc0ea4d8ad3a7798e242ba46d2adbf7adc3e0a |
| 12-input-output.html | ae0d649505b1c9536379aabc37aae392ca8c4df9c80d9a7865d8524ad03a633f |
| 13-built-in-functions.html | 7a4685c6e19283161b9cdf536513496dfdd796ac1db19f761231fb050ee92071 |
| 14-graphs.html | 89ea7b9d31dd05225e42d68282299ba62a38b2d524110b40664eea360a032498 |
| 15-vectordb.html | 0c432163c67eca371f7ff51f2e46f4b9b9158f939c8e3d3de4cb67c8e440c36b |
| 16-database.html | 17ff440da8bc10303554e3b4dd1d7842ef66af240e5f2c7e86a3d47545dfe268 |
| 17-actors.html | cb19805368990ff145c5a6693b3db4579cda90af96d9d459503090528f24235c |
| 18-agent-orchestration.html | 13c92022ed46d7ad1b7ed8424c902d1a67f2aa3ec1177a090566d616d605f024 |
| 19-graph-memory.html | 17c10e1eb1dbdbfcd9a41ba3b3c6c1eaaf887a3dd7147db6972ba8c49b7bf09b |
| 20-mcp-server.html | 3a71250f853c43eccf4b2f27fed56b75b3b0905a87786fe7c536e177335e45f1 |
| 21-acp.html | 661e22c56f90ceee55edc9a6c2d3bd121e5630b13373b049fb519541dc038ebd |
| 22-durable-workflows.html | edf7d6066b81406142b23a98afebe19fd8f7ea17cd4472b064e8c60aa454d5c7 |
| 23-agentic-runs.html | e38ea9174bc6b8fa113fe603d75e1b045a074df2a1ec32536f6902fa3423f737 |
| 24-web-ui-hub.html | 8b948c85bc0159a1cfd2bffef191a99167f98591ba6f2f83e21f1a4cc5528582 |
| 25-web-ui.html | e775e04b574f8d262114724b4ae97097fcf3aa5717b664afbe95bb4152b1acc7 |
| 26-http-server-html-ui.html | e8c00a4d053ddf8822180887dbc251a37cd0362f155b660e3713dcf9a619456f |
| 27-browser-javascript-backend.html | 9cb8a3d56b6e75ee4a99164f2952d286c21890d64d278e3af7a2f85448476ceb |
| 28-rest-api.html | 738515dd350d557f7406df2fc990bc6dd6b72d931362c9056af151a94249c126 |
| 29-rest-web-client.html | ca4f6a0d8a49c07e596b2b705264b65d7d0b299078d005432bf7c03282b131e5 |
| 30-full-stack-development.html | acda62cfc9c31262de8ad58bc977cb4ff69fc20f12173bbf366f893395eadeac |
| 31-dotnet-interop.html | d7be35655689c5d275ddb2f62c873ffb6a6697f286464dbfc967e538c22f5352 |
| 32-device-integration.html | 62168e67d2677c77e1589d50010aaf63dde970545f4fd020c99e4127baf4c64f |
| 33-personal-assistant.html | 850067e207348bae642c399b3711563fb14aa27733d567d1d9463c6ed6a11166 |
| 34-examples.html | a2532955e9bf83681e7a88dca50d65826511d6330337d7da2ba92d27b55b5446 |
| 35-property-testing.html | aa0fcc2a98200c8c264b8b58275f72599be88c277df83a8523cc32096116c857 |
| 36-grammar.html | 9e5bb67b286935d018e71619357432152a30f8e160ac0e15ac98751007f5d1a8 |
| 37-appendix.html | 0da7d634f2f6ca9d5d0c6e00e8100c2175089b9e0d67fe34bce3b80ff69717e9 |
| 38-appendix-gpu-billiards.html | 9079ce6d31254750fe7e603fcf4a6b4967f0224612ded5b0d537231bc639a28c |
