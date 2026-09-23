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
| index.html | 64685ca072146325c3c5fa848b559414f90b56b2627841a1f60d31c078b751b8 |
| learn.html | 4cf0e3276efb2148781749653e3152047406ba761ed6c0fcf3a16aa73a6085dd |
| 01-introduction.html | 26f94866b67d6a8d3b201a995a4219a51a37b1a0ff75fcac26aee8cfcb9b44d9 |
| 02-tools.html | e59b9547f3a592ce3c43434abc32c96fe69cde870c893a1118203a567c7fbb0e |
| 03-lexical-structure.html | 9eca3eb4034912d3017a58cf6e10f5075ac3e20ec5826201a91b95f582dad5c4 |
| 04-data-types.html | 1ee00c953d3a66a46189bcec64cec9cd62a5b3ac55dc3bee2c5a3131d1bd31a2 |
| 05-variables.html | 0c48dc7eb4d80c49415bb244fabcb8b439b2c47e1c30f972f610340fd306ffb2 |
| 06-arrays.html | 88c346027ba2556396e99a4afd2e340c0fd40d72491cf803d941e178cf4e8cfb |
| 07-expressions.html | ea31d5a300134c7ddeb176e894aec705c1c07b3756878afd915190a2fc12ba94 |
| 08-control-structures.html | 51c4904431bd5122dc2deb48f9797edce464391afbfc5d5bbc7b59cd02575653 |
| 09-functions.html | a471c6d6740104f9c63d126f6d021219248bd79eb0982d2ec40dbc3ebefe23df |
| 10-prompts.html | 9574ecdf005011a4d73127848518a0c9a310c000b656ca05be6a1f4932500931 |
| 11-classes-objects.html | 955e958fabbdb33f9670a567d18bab004d23037ba6a619c84bac7612b60f7eb7 |
| 12-input-output.html | 79799ef2dcadeee4da14f2d1240200f5b7fab9e993bb16eb0ba395f4a56eb89d |
| 13-built-in-functions.html | 7dffc7467d74c8fdad2c88488d5016946d370bf72ccd5467f816570eb78189a6 |
| 14-neural-nets.html | aa4aca756d8f844ba173ac41718eefd0debb4f19b1d934377a796c263733e828 |
| 15-graphs.html | a8f8f697e3ee38ad7edde5a9af32881daa5a33065d3fe7b4cee29bcc08809794 |
| 16-vectordb.html | e971314962184785cac6086f4f10b9c2bbd2cbea2111862d0cbfef89ce8e6ecf |
| 17-database.html | 3cc08a61072e95b218a0d027f5df6e45e5007c9c7547bd028284f0ba4bdb6652 |
| 18-actors.html | 1c25ef042b25a16b9cacae75f840c29d96549ab1dbf02190b134e88a24b51263 |
| 19-agent-orchestration.html | 1843287adee4276e7689ab7c26863cbc789bf93695c7471e23014a5ca7fc8254 |
| 20-graph-memory.html | e30aaf4738d4e660b6883d9966b4d24f5fe8ab93191d1091c7a702f1a10c9eb6 |
| 21-mcp-server.html | 9e1dafe21d93e00bcb923168ea374169da7f4763f04f6748eea7442ef4935326 |
| 22-acp.html | ebaccbfd1eec214e0cb692ae7f8c37b02aa2c6d548a3123f59479f993d2cb6e8 |
| 23-durable-workflows.html | 77efa94bf9f6d4012ad50a6798abcd78e8a3ce948a359422094275176ed0d13c |
| 24-agentic-runs.html | c1aa4cbeebd1dd7b0d7d84de66f2945c72bac847ad3ab8995a08336baf279ffa |
| 25-web-ui-hub.html | e7ac43857746ddfe62d6b320b3444f608a15aa58de86481e42fb9b33d77b1d1c |
| 26-web-ui.html | e7976e6af29abab2b87a0d613a2beba78d3439c7974b6c2bd20426d396abc9e3 |
| 27-http-server-html-ui.html | ecf96852477e750572436d8f39a38192b81114ce8b0b8689730f7ca58e79cfe8 |
| 28-browser-javascript-backend.html | 5f68399ff1014b81d6e6cc411765af94f6666ab9a77ac720aa31766c96da8739 |
| 29-rest-api.html | 1c3b7f3995c4f857b48864d74ecb7fda134dd13cae888d15d4e2d9105ee33c44 |
| 30-rest-web-client.html | 52064b0c3ed02d7472a2c60aada729989c5f536cdea2a1c629b94db6c5ae77dd |
| 31-full-stack-development.html | f79ed6cb9ad776b4e0353c81707343519d14a04450526bfc9df35b7ddc0b3114 |
| 32-dotnet-interop.html | d7c5cb5f26cadee83515876331fe4006454016d644ea673c2b373668d2474312 |
| 33-device-integration.html | 5eff033f31ea8bcc341420b8e541732f860cfd8dbb1457e49d8bd5b43afe98e0 |
| 34-personal-assistant.html | 63b46e8cc70f02dc60b757b12114852dbe51f85265c57321c35d078623525da8 |
| 35-examples.html | e6096882dddcfee769aa37239b413ff8b6ca725dc3e01f34fcd2d3c152686f14 |
| 36-property-testing.html | c55d77cb1a9c6adf7a8713ae69d7c931aba4a656700c157c2e7f9fbd9898ac9f |
| 37-grammar.html | be64116c764e0a5425097906af3f708cb068293c6ffeb79b7f053d1b3ce2eb4d |
| 38-appendix.html | 837acde762597c44408716f8b72aa6f84778d1fac795a9639d2368d3f4931194 |
| 39-appendix-gpu-billiards.html | e0957df8dbbf0d095fd944dfbaea81b9e21c29ae867bb7a7d6552c6d8441a607 |
