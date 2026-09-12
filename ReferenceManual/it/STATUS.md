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
| index.html | ed2ec27d378e813e76c14c27c514ecaceeda948fa8b60e3cc8e04b2fdb2cca75 |
| 01-introduction.html | 5326bd54e6f203ce5d0eb0bf30d975c8220fbc9509ab391772c03d260304a543 |
| 02-tools.html | 13841838a0c0596a026ae22a966cf317a9bfcc97401d8c9577e41b7153e19f25 |
| 03-lexical-structure.html | 5fe84cf84ef8f0f28549c7120af2975e25e5d1f8868003744882f5a515b48113 |
| 04-data-types.html | 5b8d3855fd1b6cca84cd70e6d93d94ae0c78250fd13ebeafe94de5a52049758b |
| 05-variables.html | fdc36bebd41aa2c698d45f3b07421be7151cbd74e70e71e389dcd48498f4bae0 |
| 06-arrays.html | 361ef4ca0cb0fb1e4b21c2bb30dae0ae985b54727e3f12f98d69361bad8b078f |
| 07-expressions.html | 4533fb4b7e30b92739e456f777a444825582822decab5c6f8d2d0a4ff8d8b772 |
| 08-control-structures.html | a7dce8f0a0c17a1a36f37de007bdc8684d2b9e3388111ce3df38f493f35cb554 |
| 09-functions.html | bfc36bf24000b8d223f136dc2b1a05d67f68978d8d17ac8e634342f0974feaea |
| 10-prompts.html | eb2c0dc586f5cc15c3c65af78befbfffde01347c5465dba4a2485294b0c6f9a7 |
| 11-classes-objects.html | e6e8c24a3bea7a1a3c7c37c8d99babdab7e259ce0c59b2a697db0fa38321cfeb |
| 12-input-output.html | ed3fb9d438fc0f847bf231af035d241449a455fa47cc3d9e35cd6130b58d910f |
| 13-built-in-functions.html | dd6ab54a7727659d3284ad9644e15789b38eb2e2dd8189174e4f923a7a3f0667 |
| 14-graphs.html | aa57872f357cc1df0463f0020e1a6ea66ee0d542f80ea4621d30c500af56ca06 |
| 15-vectordb.html | c95110675a56912eca0f8763af981ab2ba6e6eb71a7be1d0d82734ad3a5a8760 |
| 16-database.html | 02e590b662a39562f93905cf63d11e51e6f9731690d3278ca191ec0c9276b00c |
| 17-actors.html | 9e736f97d9e7579c5b1952534483b358868f94af35b752dab30b7823ac6f9c3e |
| 18-agent-orchestration.html | c2f6376e99a0eea3d1fcfce9286870a350a3de91c8979a600aeaf90f33d86b8a |
| 19-graph-memory.html | 85b41d1b73efb75d1a1535931b4b00c6193f108f69488e94e6629617b47623d7 |
| 20-mcp-server.html | fb949d0de379bc97eeb63b87343649f58febba9e6a9cffd72db0b13290d43d5a |
| 21-acp.html | 94ae4239ef1f4c2f02110612333b51efd89768d5e1a97670cbd80719d5c9b5f8 |
| 22-durable-workflows.html | a20586233ebf1d323b6e2a1c22792daf65757280cd4dbd6817ce520cef1b3311 |
| 23-agentic-runs.html | 0cc9ec98f91e2d427e661b734a060f23dc7426365bc9c4d59e9fb18f655fdaf6 |
| 24-web-ui-hub.html | 9cee2d2ba183916658442dbf4f825af87a9af2bfbec3e2ee93c40fcec984a542 |
| 25-web-ui.html | 33189783a02c4e8aa2bc065c638400b255aac7a0a44b1d39e47284d2be6f8b79 |
| 26-http-server-html-ui.html | 99277b6534ab451aa9f423df39fde22a9db36678931a2dfd7affe739b2ddb8da |
| 27-browser-javascript-backend.html | f6c38e05fa884ebc54f346c48b954c759e66798bf2bc4af913244e7523887daa |
| 28-rest-api.html | 13368d8deb38c4e4fe3850d761f3718c16881c41771950246dc40464a4f231f9 |
| 29-rest-web-client.html | ecc9a67a9378a116a101add4446c440a8cb9dcb4cecc55930676cec024ad132e |
| 30-full-stack-development.html | 1c17a8b8f83f65278856cf2311b1836299642b710646a9b54c7f57d20e72db74 |
| 31-dotnet-interop.html | 0d141eca04bf37f54b5f708af1536c5b893c18612d10efbdef9ed50afde521c6 |
| 32-device-integration.html | bd8a620a1f7357e86d028bd88a13214a69815b8f6c29b3e4f44b9ceed1b88c4c |
| 33-personal-assistant.html | 12cc2d16342414b2325cd1a39c86d935f05614281fa372f8339ff3199a80725e |
| 34-examples.html | 7b1b4ca6c633240f9c7553011447aa36af7110b8a2c3ec3d6c17a14c533ef056 |
| 35-property-testing.html | f841e4da3417b64fea4fd0ad66aac42f46f50cae50a0113c869c6e840dfc9e22 |
| 36-grammar.html | 86b9fb6a49d94ff8fdc5f68e6ceeaacc14112a3839b5e9a661266d07cca98ca7 |
| 37-appendix.html | 64f31b064b3d7c07c614253e628f15a6679a91bd372f4ea6a50bda04b6a9b542 |
| 38-appendix-gpu-billiards.html | e26d23a5c6537436eebae745760e889481887fdd5f6d634a8c53e3741bffb276 |
