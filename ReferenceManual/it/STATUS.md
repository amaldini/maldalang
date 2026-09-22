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
| index.html | f11b736dfa3d6870825abb4b314b43321e7893b97ab987d00116dbba1eebe531 |
| learn.html | db740b93dd664beb576807ae3a17676bfaf875689f81eb2cfbddf1130e3bda07 |
| 01-introduction.html | 3ca9dee1c40e151e6da8d6027d8a9e3b3703f8e05a49f7fac563996b0250a912 |
| 02-tools.html | b1a5433dd9392f696a7168080827862c768fe35f24bc8f144391d84e0d13cd1d |
| 03-lexical-structure.html | 03434e840ad1732a0135b787da6c4e148f1bab711f48bc314104b03a522bc143 |
| 04-data-types.html | 9e4915a91737b2e466ee959ca1b5610ab6c96597ec8523309be87dc6d9b1c256 |
| 05-variables.html | 5c45a8f496df0cc641727a14ea06cfc0626b9e342dbb43404e3e6dd5d77a9f89 |
| 06-arrays.html | d7fa901bceba9eb298bb4681f4fb78afcdaf0cef98e31ead3e0138bd06e32e37 |
| 07-expressions.html | edcda07f1916e5d3982c1b2663a135267205c39e06af91a327bc6fdc7a9be677 |
| 08-control-structures.html | 4a1ee75ac419950d06054ec579eb7dff5a9452f920d0bcf554b1a5e3f16026f0 |
| 09-functions.html | 9174bcac505eebb4a8151f0083948c989d44dfc4e83560b728cef8b3c618136b |
| 10-prompts.html | 3d19298943c811cb9ffd5990f97b35be0785e767a4c416f5bbd2183ef78584c1 |
| 11-classes-objects.html | f943a7a515805c4f75c8d413fade6511c5876aef553aac5b584f01bf93223102 |
| 12-input-output.html | 3f74379bfb404a7a26ac8f33ac8c657040539e7866ece10465005b09d68056cd |
| 13-built-in-functions.html | df4650646e1013ed022cf9a8f75b4f2c8a68377b389031bb5b6fc829edb26a4d |
| 14-graphs.html | 3e8f8633a522bc61887834e3c1ad4c664428f3b17689a4bee2ea5a131f55c04f |
| 15-vectordb.html | cf0669dab70f4439bc2699629b9e4f9c253cd2accf26cac562652fe025249eb1 |
| 16-database.html | 416bcc43a4921adc5b0f7795bb4bbfb0f929942673d2500b3b667635e0576b11 |
| 17-actors.html | 8415534364cc64cf7736a7592378aa9c7ab8cc1fc6208ea339538ab5b06ef3cb |
| 18-agent-orchestration.html | aecbea16da83dcae6d67443e646f44528324f7cc44865f7f0ed2523de980c378 |
| 19-graph-memory.html | 5512555ecfe2516e8f6f76ef25e5fd2edaf5c02ba1509aef5db02f5ddf431cd3 |
| 20-mcp-server.html | af9ab01b1b9d1db427c8c5b6150aa72cee04cfdf021396aa4b1c2ab6b5136a4e |
| 21-acp.html | babc882ac384ebb34f3a371f741e14f3e25849eb877aad430095f5222e5a3513 |
| 22-durable-workflows.html | 26b3612eb83d31d0b7605d111a72e1e1caf450572d4939e75fff948e2348f8fe |
| 23-agentic-runs.html | 85dd526e939d1d9e42c6efdfcdd1e8836ebde595d71d088911e31ff5b7880efb |
| 24-web-ui-hub.html | 86a85e5cb904972e161bf67894fa8024c3110f018a307d7aeb78ba15dbdd550d |
| 25-web-ui.html | 1a69bf6d3d4f538ca92630a1687a2c425a9b63c562027b2fd3396e1af4659c47 |
| 26-http-server-html-ui.html | 898f273deba90d61a73b8ecc95c3ea9546ceb7018e92f96bd2879b367ffc1f50 |
| 27-browser-javascript-backend.html | 2da93ef6043a6daff5a393d595172843ea95ac20c1029b31f1e72097e89139db |
| 28-rest-api.html | b124ca482b214071bfaa79d1f875b51da61838116c6791788c0c607dee0727a4 |
| 29-rest-web-client.html | 26b77f6e0bca254f7c853958f4c8926568ea6219ee0297f911e76f6032142c80 |
| 30-full-stack-development.html | 409dc6ed4db7b65ad15bf8ee7343b33bbbc03503561612cdd74a7a35b7e6b6e9 |
| 31-dotnet-interop.html | 4b44efaf4be3897262442cfddb1d0fcf1d25a9ad6ef47dffe4826bd2d084d917 |
| 32-device-integration.html | 82ee125dd5af15257b55d6a26ddbd067fc6dca48f5ff6d7e6cb7267c282b49bc |
| 33-personal-assistant.html | 08bac49ed1530d068e361849ef3c29f1a2418723dda77eedd6647ba9d90218d3 |
| 34-examples.html | dc43dd7c06d8bd9612e0b7c7522ede272d0416bf8b168c734d53133ea2493cb5 |
| 35-property-testing.html | cef516dcebcd15ffef1ad3eb78fdf8bc22a4fa38855b6540924438fdcd46a938 |
| 36-grammar.html | 2b7968a3d36285618c6b69ce860d48477181f1ab56045aebffd39069d7cdfc81 |
| 37-appendix.html | fdcf99eadf2f36a383fb7b227a3586c5ad3b6331fa6abec0da5ad37422bc7410 |
| 38-appendix-gpu-billiards.html | da3f0e82b744cea72d7e46fe347f999a85f8af4468e839c23d678941f4b96713 |
