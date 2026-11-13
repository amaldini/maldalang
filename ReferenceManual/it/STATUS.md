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
| index.html | 64358b752465bfe15dce04528cbc17326ffc679297e3988c26cc1e0ac7732be7 |
| learn.html | e0e4cb1802ccbf53494d7aaed4f7374c104981ce2b5825415c1d9de60c0b9f51 |
| 01-introduction.html | bd60e0eaff0a6cdb8f94885672075f208be9f2d63dda96a05a586646406306d8 |
| 02-tools.html | 36bfdc5737c69e532cadb7ed4eeb8d4f3708e16f06869ca4ba9e8398506f21e9 |
| 03-lexical-structure.html | f06b12110d167aeed1534faa095ba4332bd03b5eb91c01613af9835ac60c0580 |
| 04-data-types.html | ecbf37af61c6f3cb4e846c077112c91eabd7384b29f58ba724946be5ee7fb30f |
| 05-variables.html | c4be80f5adae40f275016cb8b3999169dbd793732abd73c0e7b616bc5723a5e9 |
| 06-arrays.html | 480cc12dfd6d876efba4ef1f58aeb0161e5dfb281ed3a3f5adb47654cf7b8a9f |
| 07-expressions.html | 8060c77d73806610798127d458f9fc68e9c764e00f29e179352b317efe1f6fca |
| 08-control-structures.html | f26cc10b636be231c6ed50053c968a47d3bd9e0f16ee08a0b1784027e6faf080 |
| 09-functions.html | 7de40546d774ac7f96cf90e91347c772334b0c1c543641deb392d74a26d299dc |
| 10-prompts.html | 9d87b839ed25a5e7faff5be573bc08d7e031559bbd15870ce83e7031ca271f19 |
| 11-classes-objects.html | e81e2895ce57e76db7f7675b53ec500b9f48e56f977540c2ed2c17853775690b |
| 12-input-output.html | 1a1999a799b03364440e87c47fdded871504381159d58472d7a8eac6bea82ea2 |
| 13-built-in-functions.html | 2bc293043addf405468c09a1230785ef9a3337309aa47b5508bd50da036e747e |
| 14-neural-nets.html | b84119304f9776a9b3af9ddc4446c6a8dc2de25af0c43adbad37067b4f9ab436 |
| 15-graphs.html | d95ef98665c485554eae05eea2c60b741261961a09fcc3d68399b1a7b83eec4b |
| 16-vectordb.html | 9db8cb66841d3e647ffc77fa72b14b4f36c54cfd2c820f101c0115ddf96e9036 |
| 17-database.html | 5fce96551b5c9767f7cbd86c62d77c2b7ebda2373bf3fceb36f8422bdaed76fc |
| 18-actors.html | bc6e53d955cab0673d073c1e3e6f2b7ca23efccc1adf77b0be61511e18954efe |
| 19-agent-orchestration.html | 7a343bab0dca2c2f501ea227d4ab248bc3ff5a1502b5f6ce47c38754b79ee2ab |
| 20-graph-memory.html | 17241a2ced3f2eeb9f94407c667d27835c1ba095fbb824507f073dd3d8c88c1f |
| 21-mcp-server.html | dd3a101049b433defe19d421991c06462de5b8a138e378fbac4974472a5f353e |
| 22-acp.html | 4fefd3e9d3efd56de96d131bfd23d83099e28ffeeb813404e2ed24eb9dcd840f |
| 23-durable-workflows.html | bc9574abaa903e57e17d129511df7d99e92e5c5033b53efd3a43ec013f5867f4 |
| 24-agentic-runs.html | 8b868a09fb1dc72912306fe1dbf53bb9c2a4bbd440059a6991990a82632cd743 |
| 25-web-ui-hub.html | 0354c8ca9cb0f9d2462fc82ded8cfea3da787a6c9af4feb416823990d10b52b4 |
| 26-web-ui.html | 80ec54c6ae01a4e69b7a637f47f0ec70acb600c83cb01665c8146a108bddbcfa |
| 27-http-server-html-ui.html | fd4beb60685bf320ad1dcb4a0dde424e93ff9735cfbd4da53856c8856ac8d283 |
| 28-browser-javascript-backend.html | ff44bfb175fe02d8b0ce72cd44ae58ef9f066a445d229e6473f9fec9b9cb1444 |
| 29-rest-api.html | 51051bfba929adee4edfe33ed3d33a165e981e2613fb64a885fa768064386d71 |
| 30-rest-web-client.html | fb415cf699fd403116c1a0cf1d58910cdac8fec1c9202bb5756ead51ffba4ac1 |
| 31-full-stack-development.html | ea7d6b4eba172deeea10597431b43c9b12159146ddfa589f498bce7dafb44a99 |
| 32-dotnet-interop.html | 4f04cc97ef7a5ceb3a88a33693c8ffb49837ce29d7c74a355d02c43901c7ea07 |
| 33-device-integration.html | 9988df3c9745917c0befe0f20e5a5918fd4e9c31006b8d648b5efa7b01719804 |
| 34-personal-assistant.html | f39cab98b8d3dbaafe89e8060e8b916ffe729e130d0fa2f8e87c24553ae4214a |
| 35-examples.html | 42e74ec81976553848a6d4d8a9161559efb6dab63a9642491ff1dcd343e927ba |
| 36-property-testing.html | 65c240b184e4dbe9a3ba25f39827f538e31ec0bf6a99cea5c5e9fb7aa9e5439d |
| 37-grammar.html | 6a7a3746e602e4cc41ed95164e617ad7c0c6f912a3ffbaf85aed6ef1c5bebbde |
| 38-appendix.html | 0c9e326c56f9c7867e16a57abd1e8e2540832c54e0c8c3b7d8056a675b1faeec |
| 39-appendix-gpu-billiards.html | 618fe55c883e96c36adcd64f78ed0c29016acbce92c3a809ef1cc55a8fe92fdf |
