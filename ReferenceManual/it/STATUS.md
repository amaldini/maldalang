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
| index.html | 9623ffe02478907394d8b7916b97bb721a9f5e7adf8f0386dd36c94360588b77 |
| 01-introduction.html | 126488a489fb4b3de4ef34e9dccd4c9000354f86525b843af0a69fe63865d0f9 |
| 02-tools.html | 6255f433f86b813acded429d92f6a0922969172ca70a90cc7fb9356afeabb2ce |
| 03-lexical-structure.html | 210770bb4ff5cd6ec082167a07b0cf280c8ceeb2921bb115a6198caf36ae80d3 |
| 04-data-types.html | fb8d1de5113aa27bbaf36dba80d41eced73d37c2a0f38c75088eefa95bd6bc30 |
| 05-variables.html | bd2a0a542c4c8d81cf9cbc829fc2f512f4eb712f0e711d3f9796144b680ef2ab |
| 06-arrays.html | 40426c07358c4f65da5bf05098909b0d5469926f466461e3ff98651fbb154d89 |
| 07-expressions.html | 3b0d517fb6e1657125fe8b9b49ff901e51959c021ae588417ecb11104ed0f715 |
| 08-control-structures.html | 8c495437397a602e4ad47c91a8fb17ab242303fab573e7d556ea2471e7beb55b |
| 09-functions.html | 10bca8cff7c853defa6c1306576157e043b93e7c02814e813227c56357ba1a14 |
| 10-prompts.html | a89d1503855fbbc2f22937571910ae155de5ccc2c86ce9652751edae351748a9 |
| 11-classes-objects.html | c0e443b9ba057cd21496552d2401c9a666a84592cfea799cfb97af3a71050fd6 |
| 12-input-output.html | 4e63c89ec342f54405f57b4c4317f4a5f38b3a3cf5f872d884c0cf08c0b453c5 |
| 13-built-in-functions.html | 5b53fae27f3ebd0c6ea14f29591ecbdf6cdea0c35cc8d2f4d6974833a6ce9bbc |
| 14-graphs.html | 24683802b6bc04a07e73dcb331154748e964e20f38fdadd9029f166c8f2d909d |
| 15-vectordb.html | 7df987ff72b0caa753597d6c2b1cc0ca76929007718c89215c55692b3c8a27cd |
| 16-database.html | 60e60546da41f9d78a69f913c5ca7ce3b3635fbc6f95689efc2da348d0fb2753 |
| 17-actors.html | d1d2fb0ab2fe25f04e556b4c35a79db1bc5762e2cf1e56e39753a168275a9648 |
| 18-agent-orchestration.html | 97fadf65ef21f8ff4e9e285481eed5787725e8b8b62eebb1b6519cf9bd0e9079 |
| 19-graph-memory.html | 46f0abc6f8b940915b6b4b71c51d36b78cebe0e06f38c9202c669c339cca4ae0 |
| 20-mcp-server.html | cd41b08e9e87d17f2563e9fa3fe98f1088be224d699948c67726fc6cae3b97c6 |
| 21-acp.html | 3378bf6b47353be6c447f7414ac738cc98624d047459895b999976a8b55c7e19 |
| 22-durable-workflows.html | f336193ebfbd28841ab9308b269a30e693b91c9c0028a184e6e17c49744e44ff |
| 23-web-ui-hub.html | 6626530ce8556c5148561ffca8a8d5aa81163db582501157a7da752a8842928a |
| 24-web-ui.html | 95466b4aa4894fa4c050689e41d2e62ebbda5c0718bb579b3292c8c8b127777f |
| 25-http-server-html-ui.html | 23f49514cac73dc8ff0ab5a5ee062d5549ba7dc03c3bca1ad309605d0ff73c6f |
| 26-browser-javascript-backend.html | 2d79beca403a460694363d979e5154d01eeae5ed8ac96b3898a26dd5355cae2c |
| 27-rest-api.html | 6cbcbf27fb45902afcda1a311c5123587e28bbcbb3f007cccb4d13873fa59683 |
| 28-rest-web-client.html | e23fd3e6b3173bd33479f27abc7c521fbfcd5b813a4d92e5722c0034a5bada08 |
| 29-full-stack-development.html | 5fbafda1566d4555fdf22c4e490fdc7f2fe7b7e2026cf76151de609ae6c08e36 |
| 30-dotnet-interop.html | fda7ffb35393570834a8328e07d22d5ba5e3213d315011522748410c75ba3fdf |
| 31-device-integration.html | 550de93c2c5afd871e8e6f1a13c2ae481476dead23c0d7c444239fe8c8c12981 |
| 32-personal-assistant.html | 9b2219d61864194bbf12876ef5bb75959926efeeafe82ecf76c4b46d3a2501ae |
| 33-examples.html | 68b80336e96fe81fc6d670aae8087db4cff799f20ed50b052092cce4a61e7d89 |
| 34-property-testing.html | a53796ddf24a028498d44e68e297639773f9621135f5ae713d0b68a7675d79e2 |
| 35-grammar.html | f77e4d128416041294abcb5e2e2d305a6b4fb47223bcad55aa2dbd3515477843 |
| 36-appendix.html | 70d325e14df6ca23a6b00208bebd161140068c03be6f94b1e2e64088dfb1f659 |
| 37-appendix-gpu-billiards.html | 509b26599257c63b2c6aa8d3926dab294415795d3ffe7d774b92dff2e4bf01a0 |
