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
| index.html | 5432a018722955ee86e4de5f74d82cd964890f2abe094f05c9e830943708050d |
| 01-introduction.html | e8fbee7cb33cee5908fa2721d380052fb3449a7dab6cff2962cd71b2bc41f2fb |
| 02-tools.html | d24275505d155678ef84e8b3824681b9da248b16e9d6cc836e8da92c9710366f |
| 03-lexical-structure.html | 955d153ffd668ebcd2af551978f8e48f5fbe7d146cd11d5f679e8280ef6997d5 |
| 04-data-types.html | f9bf2d32cd11be43fd1e4ff4013c1803add3e53c4f761beb58b6ca7e1676bec7 |
| 05-variables.html | 80a112bb159c6598cce5b8ce0df72c94036c4125a7116639994922639f567d66 |
| 06-arrays.html | 5dc1b82c8c476e4324dcc80bdbf37f0c91c1aba67d097b3687f1aaf939e7d2cf |
| 07-expressions.html | e27a1bf391993c1a611cc3bd1db26d47b9fcf497ee9df2c743d61c996def1860 |
| 08-control-structures.html | ee776cd7e87682547845820555fa18e9d447096bc7e3896601bd506637b15a99 |
| 09-functions.html | a2124a6803d54054a2da22c2b9aee9a173d20a36a7597d325f125f02785f1f05 |
| 10-prompts.html | fc45f6267971bad0759d7d907653c3f2bf6a77e2a187489f686966a5bc3c875b |
| 11-classes-objects.html | ca6e7148ada6d809a5c87f648ecfa1191f727b8c6a9021f3497fed2d1ee32f10 |
| 12-input-output.html | 5f5f7191533be42e9f98337dc481919aaf8b3b7c8239212b2e56f6aff15c5a97 |
| 13-built-in-functions.html | 723f791790e80abfe73b1c820850d1678cbda8823078aba7dc27a7f3447d1f24 |
| 14-graphs.html | 49a97676f3636363c7dcd8c84e9b151c79ec1d081ae67d5ab4f0068b33bb08e6 |
| 15-vectordb.html | 334b6d37a3bae299fa081d96efd8dcd08cdb8c5c594f0f54ad41e6ce395fd1cf |
| 16-database.html | 79a0a7a47ca1ae23537d07456397c45d712685336941b5e57c97be6634fd04b7 |
| 17-actors.html | b1960a7b49f736d39a0cfb1d7ef609d9d81804cbdf5850d464c7f2d8f5be4f86 |
| 18-agent-orchestration.html | c45911a5e05f78df0cccc8755c40d0685f37d4946cbd51f39d05109ee83a934f |
| 19-graph-memory.html | 65d5f38f5614b5b62f4e7b18e1aec5c120a2c653201bff53a637d467d2215ec4 |
| 20-mcp-server.html | 2e58655c5520dba100aac523ff7e40a8f58b539addd017dda1c368f9b3ba0ef8 |
| 21-acp.html | 44bb7bc18c9bc84cdd6f4dcc050db07c66ee27b4c3a697bcf2f2d5bbdf8cde28 |
| 22-durable-workflows.html | e6aae71c3c2a3ae42be42cdeee8b93b318864e9605cbca568a7f08bdece7acab |
| 23-web-ui-hub.html | e25dd727ddfde955760b5af2f3638c618366db6cfc6ef0d1a366d20cbb332234 |
| 24-web-ui.html | 2d58dee8f4c27c3bdc15e74cd0e0f5f7f0b6543615d9536fe9291c2ba5f32c53 |
| 25-http-server-html-ui.html | 1daae8d68aad666cd990569fb288a131cfa716b4cb8d395a5cf0b9ce2cbf1486 |
| 26-browser-javascript-backend.html | c51b73706665297e8c884bd65353c908121d6837e509c5c0dcda59a9b2ba06db |
| 27-rest-api.html | a21f2acdb6f3144e22fbd3a47d318524c678759346da20142636f46660bae03b |
| 28-rest-web-client.html | 49c09a2ada5de8fc22abb7cccf70b6b69b02dd9cc4f067853825e413e7ba378c |
| 29-full-stack-development.html | ff7003d8c4ab6215620bbf45a80ce3ee94c9a9fd6b43b7e06ff502c46dda2d83 |
| 30-dotnet-interop.html | 1026b7b0063571227e76a47489d252c37297feded60822180d6931b11126aa8a |
| 31-device-integration.html | 4043600812e6fc7af8d30d4b398884dc38af864bfa201fbfe701080b381a7417 |
| 32-personal-assistant.html | 174ac33eb594721dd7359a8a4dbf96ac7835b88f96c33480107250c19f2412e5 |
| 33-examples.html | b0963f1ebd5a7e874c3526fe08ead0ec936430507497ea814fa35a84f1b2f927 |
| 34-property-testing.html | 53d9a96cb304bb9d400eafc0c455aa7181b314d56bad9a84739b4f93b89109fd |
| 35-grammar.html | ab116e113cd2afb033a9de5f58df3669d768db4261e6ed73bea46dc24ec117f7 |
| 36-appendix.html | c71d0ed6e58e3af8bc5caa527b030473185841631d89983f1b08cc4c16964098 |
| 37-appendix-gpu-billiards.html | e15bb805b2791d567abc973d53833de77b793efd234f87af118585da73ac644a |
