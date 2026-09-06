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
| index.html | e7a49e769b1334b3bafb807151abb6867aff9a2264fff30dcc84996e28622ac2 |
| 01-introduction.html | 35142cd9ae897225f88f1f29b95b342b4b1f43dacb13e2043dd89a403c60adde |
| 02-tools.html | 983e06ee9646f8febe3ad958beff91a1a756d5718746cf8191567d50fe77d52f |
| 03-lexical-structure.html | 3d9190907510aa1a6fb96b5c333f8a2996fd723737583732f3314f1fdd0394a8 |
| 04-data-types.html | 93663e571d109c246ffb784dc9da77d39fb28baeba88881763197b8ca547cc31 |
| 05-variables.html | 804bbff2205685b446fc553e9a8c24f13955e5e7cecd3c823f92c782be76846c |
| 06-arrays.html | abfeeedd3d7037d4fa35e43eac4b70daab07bc2b3544b0b5f7bb14ee359ccfe6 |
| 07-expressions.html | 885d2c3adead50e1c4e0d6108ae4ab80633a1884e4d82b88f0c9b121707cc983 |
| 08-control-structures.html | 2f167e93f2f9e42e574d008dc592463ba460d66661cd8e219c5a5d4302930a0c |
| 09-functions.html | 48dc306ad451c954a6fe2b865ce622f25ccda89039d331dad8cd9877ff27d787 |
| 10-prompts.html | e231312d186370165aac2519b4b091d03ee98d8bdb26ce0de1be76d128ea893a |
| 11-classes-objects.html | 9ffd6a17434b5456d96d04562d7a8e83148728d44ef49743e6feca3837b7f2d0 |
| 12-input-output.html | 88f28582638aa0fb9a5a7ee5c4300beb0078f9c051a1e51883503342d095e4ae |
| 13-built-in-functions.html | 43df5e0cb290cf939ea67ae8b5be0bba5b7ba05fc21dda6e8779422ff90c1b7c |
| 14-graphs.html | ae1fcd9eb8c811399e41d9308eb5296d5a36bcfbc644aea9fbc16d804e41c0f1 |
| 15-vectordb.html | 56a742ba47883cf9c09629daf4638a10c39923fd536b489173f4dc2d647efbec |
| 16-database.html | 6fce48d39c692911b2ad84dd5cb70642c62598cf5e487a8b442943d6fdea832e |
| 17-actors.html | c23407c1e88b34a26a4718de72899fe74e0e6bfca9caf1dd5db8c3ebfa2ad14d |
| 18-agent-orchestration.html | d2acf4f5f6fc5b389045f2c8a3ffe4031c895722c334463f1b5904f70356dfbe |
| 19-graph-memory.html | 52bd110fb295c72887f8c39fcac9efdcf47f5e413f66923d6a557d304d22e803 |
| 20-mcp-server.html | c559ba7d5e8e0571e2ff787a7ce0d28ddb5f9a4b36a120406f3b7e28ba38b9f8 |
| 21-acp.html | 7614a2c9372d1c260a6abca8140e870defcd0e55321711bbe757f3c329187eb1 |
| 22-durable-workflows.html | 5f6d9d5cbdebcdf624284ed54963ad150b49d5b5e54e35916f3de5d7d8fcb38e |
| 23-web-ui-hub.html | 04bb52aa92904f28ce26575884f2237aed9d500e1c8671e972e1d45ec9ebc6c8 |
| 24-web-ui.html | f17e9291ab95a2c94233ec4c4f0c7147e20ad2ced3207b82eb98d12941942bea |
| 25-http-server-html-ui.html | 9dfe1c20ff3e9915febc7f85859557c0814d53c7f4a3040a1f4e9cd2038f3c21 |
| 26-browser-javascript-backend.html | 5c94a3618aed27a15c70cccc6e647f98a2e27c6e6257d83c1c00c1d94a43bd08 |
| 27-rest-api.html | 50333b4359ce0b28fd53b54f1ec4f3133358971b37d404a72f5ebdaa9269e2b8 |
| 28-rest-web-client.html | 11d7b836bb3b81db42b1e17c3bb67660d1ecfd51e52573f61162268b85e2eca9 |
| 29-full-stack-development.html | ec5e27c7801e378ed9cba6fc89c29c33d989e7ec3346b46b52c17e396a7a77e3 |
| 30-dotnet-interop.html | df60ddab2a253844bc13b226fac97faf01d46b51975d92504490b9e5650d387f |
| 31-device-integration.html | 22721a34529b4831462b81f25a256338daa3a758e6f99cdb9fbf7f7ec88d0bf9 |
| 32-personal-assistant.html | dbc5baf3ab9d4a5337b9c7d34772ae61dce7d47e52bb034547dbf984f7ebe23a |
| 33-examples.html | e155ae476ac0df0db6f704a910f84eb9bb2d47f50e65564baec7895cdc61aad2 |
| 34-property-testing.html | fe367082075bbbb965ab49e8a1d67fcc94a920c99ebabf80932314dbe98453a9 |
| 35-grammar.html | 29f358fcc7aa2d8483ef4ac84d51378d92d4864fe7aa01570e9d368383d4d141 |
| 36-appendix.html | 966c7a1553e46c43ae5559af419a131959fa4d70c0dfd0aab567e500b7b9b1fd |
| 37-appendix-gpu-billiards.html | 7b9bbaef49cf29e70f5cd7284976ccaa47355512c2d49ea0be5d4545ccba19a9 |
