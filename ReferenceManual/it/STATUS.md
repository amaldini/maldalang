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
| index.html | 6f0d4f15b02cdea2e0293ef770e2882dab2211b1b3f26f269466896f8d30bcd7 |
| learn.html | 3726fa9a6ac87cbf6b578da3ec8dd864de74aa77fcdc7a42cab37af7b7b75951 |
| 01-introduction.html | 3e27b721f6d24fd3c2a517448adff9bd0c5470d632727c89278aa8d7dd63e2e7 |
| 02-tools.html | 1228daf468f2f9863c4154f5e6f7f1d610208c7af0eafdc90dc915261fa3cedc |
| 03-lexical-structure.html | 6d7f1ae8c0c66556b66299b0f70628ebb38a4133771b5063b2eb33b73ae559ed |
| 04-data-types.html | 8f03dd8c4c95f67c467fc7ae2557448a3dec99a680408e8bdaa5f6be3d763902 |
| 05-variables.html | 9a79fdf4f9a8964f1780a12349525145aab54aa332e5368c49fd7cd8969585ad |
| 06-arrays.html | fb7028f02ec85548fe3942ed2f5227b574c764568e8ed0d612a11695bca88dec |
| 07-expressions.html | e01cbda2555e9d2e4e3a2bef1ccc46269c49136e18ad70eac2e5115affecba0d |
| 08-control-structures.html | b32714556b29d543ebc02da3324cfcf99f8828aeab9dbbf0b8219d2034985671 |
| 09-functions.html | 49443626b15e4877679c4f6a8187d18d976dd2445a463e8611a016939edc2552 |
| 10-prompts.html | 9db107e1ab839c83929daf33e12baaa1e834d92867086bffc8534a7bc484a65a |
| 11-classes-objects.html | 298493fe92578a3030775f711431391be81d4b581a91236e1fa8422709454522 |
| 12-input-output.html | 8d6b369b2855ea3065b6f6a126b43d5fcb3538b9fd916cdb5b3f006adce4306c |
| 13-built-in-functions.html | bae27348f41b40f5fe8ae728588a364a0143aed6cfaaa28a24fa8dc9f46dd139 |
| 14-neural-nets.html | 8bd2e72466d90ef97832c509a6957d574698e4b79cd2ebfab47007cb8f3728c7 |
| 15-graphs.html | d8a73824c23d4b59cf85d622b0233e8a9734de710da2a33067fcdd970df19037 |
| 16-vectordb.html | bf76a3e89a3dae44910f9a7f20847cab87fb3484a709c4016f817e1e2153b9b7 |
| 17-database.html | 022723c3ccd70089250825fddbd531a3c7f72ccc74be7a15978dcc66ced3ffdc |
| 18-actors.html | 367c36a39b4759718ab15896241340826e68b4e747540e05838a88fdfd54689e |
| 19-agent-orchestration.html | b8ca36feb7ad8a888e9795b0a586a7fe25dcf7b14e3a4e4295a3cc0f258b4172 |
| 20-graph-memory.html | d9f57304317cab380db8b738527a15c7bcb2f93c0f405b8ab368401412857c01 |
| 21-mcp-server.html | 1e18f1f197c885c058efc39e46c602ad58cb2cf2f85a3259331e3d7c9e7f65f6 |
| 22-acp.html | 272ffc3604553a47334e4b4dcaf3bf3668bd84cad445be71a8ca205c6e4044ba |
| 23-durable-workflows.html | 2d8ac20f551a1f06182876ec72dda539e4387c65653d3e1f069c36dc8f845adb |
| 24-agentic-runs.html | 9e9264a3c6c7dc872c82dbcfba9eebc506987f7ccb2c3f39224a204b5f456429 |
| 25-web-ui-hub.html | 8332e837bf2caa1011d776205b0efb20c253fcc9acfdf9bfbc6d04b62cf3178e |
| 26-web-ui.html | 455e15872e11681faaa9b142aac3dbb9d45aae320aeec269b280443652be0e84 |
| 27-http-server-html-ui.html | 1fe0c9608bcad9ec4a90661c93ceb27a3f1c9680f4d33fb09a1069a67e901f33 |
| 28-browser-javascript-backend.html | 7dc9874fe3d2558456c0ac9a87a0f5c9372f5a70ea9311a07ca9a8372ba59427 |
| 29-rest-api.html | 2170d9a972628d45e429b39af4860f627395f0020acd0abcb036f48bd85b03fc |
| 30-rest-web-client.html | a30a6a29232504322b9a1ec54769091b95dd021eb547914b82647d95e4180afc |
| 31-full-stack-development.html | 8487a9a2fb8f11a542d05a952ce0e892d4431c7e32154aaab890079207a44514 |
| 32-dotnet-interop.html | 2937bba814fd388aeb9765aa2787ab168347597c11c8503b397a5b11a7432c5d |
| 33-device-integration.html | 68604827fa6b82ab005293885586136fa86e2b75a264029cb6ca42e5329871ac |
| 34-personal-assistant.html | 89ff05be54f56adbc2c0219ab2551140c8de88c469d27a7eb4be69cf751e1384 |
| 35-examples.html | 600d052cd5d90ed6efdb10cf7e3f8ad061db95e507aeb3c2bafec954327f42a9 |
| 36-property-testing.html | a4d7e2ebbcd49aa7ccdcf08aaa0acb1cd6fb686394355fd551876865351e3934 |
| 37-grammar.html | 8bd0cdb36ecf2648932987264bdf93dc6c35edefd67097704fbfce170fa1f154 |
| 38-appendix.html | 028a3e26ff5fcdc68ceeb8f486bce679cd67b17f14ef9e3add265b8b995ac73f |
| 39-appendix-gpu-billiards.html | 994dfc5392a0b11b64fc688cbc018c2eadb40f632839f28f57377dde9617a8a8 |
