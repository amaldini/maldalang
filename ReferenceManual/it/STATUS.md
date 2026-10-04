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
| index.html | 8ec01f160a861bb07c1ed778ae2cd83ba3b7a39ab79826389794e4a71de10efe |
| learn.html | 2aca430a29d43e69da91bca4d428d06ebd5b069e2cb9fd570ccd8727f46a3f86 |
| 01-introduction.html | 82f18a20be42168644953cc47110b7e12f78afbfc660ea57125205dca7f82424 |
| 02-tools.html | 8c3e009ad171c6c8c54e3c9d3d8ae2e021f904c629e18467fb913a39a1c11d1e |
| 03-lexical-structure.html | 74fd98b6e13bcefcaa49e3ab5dd339f4c00bd56a56cca597f58e0163105940cf |
| 04-data-types.html | c43f08bf36ce734b7a193d0da39bcab624e9d2157a6f8f1c6e2e07ad88e834af |
| 05-variables.html | 385dea12ac5e5ad0d9b612fe070a90da4cc9997b8f73f9fcf5f9653af93e14a9 |
| 06-arrays.html | b4e7d2535f2285e58a56e734b78cf50a11960ea2ca1fe354937492bdc765b926 |
| 07-expressions.html | ea6e24244f05d2d2f6e226932bef57121a7307b704b09442a2b49c25750a734a |
| 08-control-structures.html | 63044c0bc41a3c6920ba6d7b61044be9f126e096443c96e49c9ed78021e77f73 |
| 09-functions.html | 766515f3ab05f7f25fb5b24a2737b0a94d80f7ec328c9f444e78d011b94e5a7b |
| 10-prompts.html | 8746b6849f48f0c8406573298d5000a54389e546d1b4b3cafdef0894826917a0 |
| 11-classes-objects.html | e7153cc0771ff922007e04241dae2c3a7fa5bbdb406ea696c8a066ca98af40b1 |
| 12-input-output.html | c5e19ccfe562e78579bfa3452d15ec3736ca6ef9e010da714d28c382c2887e28 |
| 13-built-in-functions.html | 6c98b3f1f3e22de1b4273ab66da2ed1287f287de8d6b9ac00a945600f2f5197c |
| 14-neural-nets.html | 2bc6883cac139911c095994b1e776d68e7e974b8a6f49800ff9fc8a0e21a22ed |
| 15-graphs.html | ad2df125db51fd1148eadcb2a3beba417a1dc04dbd3ebdc2ac545a70ad65de8a |
| 16-vectordb.html | 1d8891e4b99953822369b96d405052bdd8ab1a63fa2e2d2ff44abf3b078ffad4 |
| 17-database.html | 3a1990b0e8a7e8bedb214d514b0e1df8b33c0a732922585976545ba249a98606 |
| 18-actors.html | 132c5153d293e5d621c4b0059b0769b3e4fc2a2f01d96be8150341bb6324f140 |
| 19-agent-orchestration.html | 8081fe742e5156ca15da8d56fbb4f273b50161248fc57decbf26c15a8bff83f9 |
| 20-graph-memory.html | cb2435aa81601061c02e767e376ed55d1995ddbb8ea5fc30c342cb53f07b0925 |
| 21-mcp-server.html | f2f3f228366902dcb835b7547e6a950bda2208db8d3a101efe95ef2a86c4b2e2 |
| 22-acp.html | 1d19bab23ea51579c008ae39d446cebc0fc1c20ba028c7bf1b71caaa99db8f7d |
| 23-durable-workflows.html | add03f4a75c72b6e393d3c7d810968e95d4d7ee062fe82dbf8ff550f5a2a741f |
| 24-agentic-runs.html | 63d9052dfbcf6810eea2fc3a0265dde84049746b324fd4d40d63ed72768d5ec7 |
| 25-web-ui-hub.html | 0f4587af2e97dc89a5ddc2c757528736a145d094a3d6c456408681492d938bb4 |
| 26-web-ui.html | bd5b71af6c4336f259f7e1200992c176732e78b4b2feb3f43959365aa2355e55 |
| 27-http-server-html-ui.html | acec9d80e7fc1d3432e5734de1aabfb260308b308e8c79b3e8b588e6b5162588 |
| 28-browser-javascript-backend.html | 5096c43ef4441623eaf0dcef4dc050b0b42953120adea84fe2c0230791f17d20 |
| 29-rest-api.html | 4b1cfbc9b7b73a03453aa184dc23ccb22173ded999768acd984f59d8bff857f2 |
| 30-rest-web-client.html | 0576f982a44af590610498c4099a01e84f6923052be87c25d9f3bc17350dfe2e |
| 31-full-stack-development.html | a4c20b52ceb98cb3f9b57034291e4a05f00374d0b51ba407beed583e869403ed |
| 32-dotnet-interop.html | 5452da3545dafe70d9ccc48e685352cb87516517a016b8934bbf25a768f778df |
| 33-device-integration.html | 45062ccace034536fbed3404a01ec8bad0c90ab7563551e1aee6b8b7bb3d40ba |
| 34-personal-assistant.html | fe9e1a70f8bbc6ea3c7b49fc8bb2f2244725ffa79710f680d6a14ce4b5799ef2 |
| 35-examples.html | 4375644d1ebc9f86956c0660f48888ce58cfd7dc02241757c85e0787fe9e9732 |
| 36-property-testing.html | e213898409843a8f4ff459efcc575410d0911db7c1eeba14e33199f4e07f1bf8 |
| 37-grammar.html | 94103dfece148920c8d14892b44473502e5534fcbe792bd64c19c7111db51a1f |
| 38-appendix.html | a64556182109596195de7c9dea58f33f9826ef3e80d304c4e4f8f39900d2f8d0 |
| 39-appendix-gpu-billiards.html | 4191b3b215ade0ebdaac53cd69f10fec5bc6673ae2d40e483393e67beb06385a |
