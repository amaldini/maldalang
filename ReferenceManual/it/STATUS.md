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
| index.html | b561c8a0fad87693e362482b5b57523362bb4bb9ef490c21dd5cfe2e5669e803 |
| 01-introduction.html | abbbdf088312f413ce516467be356a2b110703d9a3fd47de52519e9d7d2e8153 |
| 02-tools.html | 338616e3e13087532a27480751fb87afc154df2ee09fe0d8bae5a83fed3c026e |
| 03-lexical-structure.html | 291d7bc9277f0576a87ba6b1327fbdcc24aefb96c302c968007c11c4a4e47913 |
| 04-data-types.html | 4e62ef4e23749b845df57bd1924a9d065316ffc7c656ab87a10d3bcdc25188e9 |
| 05-variables.html | c79b6d4d78aa9815604f7eab27b57f5c36bb9bd89d143dbd3c9ef7ca1c6a9cad |
| 06-arrays.html | 400dba364369dce466805a03f21d041ff57b25df7e53fc6650427d397433e2bd |
| 07-expressions.html | bc2f79679427085684f664ee0675bbb639d027f21f59ab97c0eca88d5d09d8b3 |
| 08-control-structures.html | 0e85e8a274e38ae33ef8d4b2d3c5cb79313c08707ddc31a9959124919b1f1227 |
| 09-functions.html | 6c2565ec8803692aaa0ab1950af238bed0dee1977ff212dc3c08f32757355b03 |
| 10-prompts.html | a3a20ca56112d682f68147e2c0cf2e68e2e0dffa326c0ca43b61893ca648b27d |
| 11-classes-objects.html | 9a7676207981aa4891c1a36264ccb4d5af860af347680a0fcc796b2977ebbbc0 |
| 12-input-output.html | bc6e177c4d4c1dc8b2093378ff1de0037dcd0e7cfc16daf25ba69ff08cda091d |
| 13-built-in-functions.html | a197e8bc2b690cc087a2de14d6a6e94f2a2c558c3b42712de1796259e0e25e09 |
| 14-graphs.html | 8c675bc0a5a94d6f2300ed5ad4b24a368345f1d30522524dbb56d8d30c4d2e64 |
| 15-vectordb.html | 8c295088195b3414f212d349f17ebec777f0f6cd163149c2c22fb7237702063b |
| 16-database.html | dec0ed535ceee2a5199822888773f83d3ce2d8e10158cd888b2133b2acb8a213 |
| 17-actors.html | 17e025ae660a801fdf41c93c60d52c5552092331e436750908fb90c7229079d8 |
| 18-agent-orchestration.html | 83284ee97715899312155a5cdbd47e61187ed0839f4ce6bcb6dcb3adc5677c2e |
| 19-graph-memory.html | 1c38171434374ffb58f96b4894c304e0328d432b0ae594d7c0b338aeca85006c |
| 20-mcp-server.html | aa0d7115c00453329e9768f78b61afde116f69c7403da455f8c1de26654f97a3 |
| 21-acp.html | bda14e2d508bc96a788d1c18b6464c0ab12b7f4b75d6de5878fc242b2233d0f7 |
| 22-durable-workflows.html | 28a283a14180084bee9abfc1e335317e04b7db0ed30519dfcd2ab64ecea9b35e |
| 23-web-ui-hub.html | 3e78cbaa692e70ff442e546075164e285b9d39bebfa6762ab087e34acdc013fb |
| 24-web-ui.html | 4d229bdd239be87831f414f21e427dfd5b0ecbb3db5918f86ab874f7030435aa |
| 25-http-server-html-ui.html | 7df153b00d73cd5ffc72ba06c59dbe80d74305ecc942229d5274c36ee18f7b5d |
| 26-browser-javascript-backend.html | 4db3af32573d622f25a21e61c05f18cab9a3c1797abbb6a08eb92870d79e7bec |
| 27-rest-api.html | 6fe4a08ea3557c65ff1e91a625bcc666636a6dbd8462e7da3b90e588c9990cb6 |
| 28-rest-web-client.html | 9465b54aacaf58d3762c32d9c21ec87c52f0f1dc922d6a1fabf89d8731a88724 |
| 29-full-stack-development.html | 7fc70947c4d3fcac1349a0af2d6ac53bf2e87d68f2c9243ba0dc9259377998ca |
| 30-dotnet-interop.html | a421e57a2eb7cda0b5c309cb68e85f0e086ae53ca436bda478aba43986459e83 |
| 31-device-integration.html | 8b7d5a8858b859ffc69be0eeef2ce3612d66961774019256a7164c825e96a189 |
| 32-personal-assistant.html | c52e868f58397ec06c942ef83b07d041c91cbfe46e12be7beb7b06bff50cdf2f |
| 33-examples.html | 03a590518ec3412599939ea35c6f1d730e3d4b7645320f409e266681931c2246 |
| 34-property-testing.html | b46fa84d08a00e138e50635ec5dc4abeda53e621a1d92e270c3cb31ab92bc811 |
| 35-grammar.html | d5e0693b01c5a202655735fbcea7d8804eac4b3b74ece85b70035836eb565602 |
| 36-appendix.html | 1b8d050de164328e727c08b2f1101cd73e80f15ba5b68f5bac66a59b73887178 |
| 37-appendix-gpu-billiards.html | c56698795a90108a4dd8d129d48c02926eeed8b2926c8e5fefd1ad4b66f4c184 |
