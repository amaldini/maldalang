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
| index.html | c299101b50dc3ae28a8851041c5a7b86c2163e8238629cd218b835538d374e3b |
| learn.html | 12bb8317973e300a122f05291157be0cf0a296424c0be082577daa781cd0d304 |
| 01-introduction.html | 55cf74dc434dea30f5df38a6a48f42ba757cde841eb21d384c9bf4e72f4598ba |
| 02-tools.html | 4699f9e198ed9d139742c639bb4eef265ee629260f7efcab0ef8a46ce1c6bfd6 |
| 03-lexical-structure.html | 63e19f0b3ff533ad0a9184b30451d884bf0a7dbd2c4bf50c3141ce85b769101e |
| 04-data-types.html | a80843277f8e55781ffe854f2981958b33ddfe09a1a9c0cdba09e274384dc874 |
| 05-variables.html | 581a4e715bd3010758eb91fadf74f85d5133c24a6f6e25d9cac3e645d169ccfe |
| 06-arrays.html | e991e4ab3c91f32106d322ec76e5abfc34a7887b8e601c6afa913f05c123a9f5 |
| 07-expressions.html | e498f5564c7b61bd6327ffcf734faf6b1b2557170cc5b931018fd966a494e192 |
| 08-control-structures.html | 0f32c3e7b0d25ec616d4cecb02eac27468ce2cfc3eb32b8bbb00c0525e4ff2ae |
| 09-functions.html | 190cf142c3f2c0f1e593e4b470c2d7f01bae4d79c3cba657cf185cfdbf7d9b01 |
| 10-prompts.html | 68ad26a5a584e9139454d7c0c0f18abcc31b4f03e2edc6364e5e15f71f984cdf |
| 11-classes-objects.html | 1219b953064a5c6190997f1f0643fb25895a478740402ec0cdbc3fb8a85b3e40 |
| 12-input-output.html | 991172e7bf3ba58f14a6b0d2c29b408e1e882a3931c31d057b1bbc07e427c00c |
| 13-built-in-functions.html | ae2eb8a99d13434b088b7f9dfebc4abc378b344e1719b966c9507a3cf8669ec5 |
| 14-neural-nets.html | 3b44f4b3956f7c177b2177afe64c1711fbfd87fbbe6e6783dcca2ef57d84d697 |
| 15-graphs.html | 26dea737e1c21f1609436ef74fcf13385eba73886ef2de3304c508f68ca1858e |
| 16-vectordb.html | e30bd44aebb0662e1c5cafe8b36dd85b0392f3a7822a7a3bcc7982b2820d503e |
| 17-database.html | c77d75fe61f84a18d7ad6ae2b89bf8f322f5cb595ef807b09d2fe1446ed247bb |
| 18-actors.html | 3f65a6aec3fc66f98555048c2b64d6abcfc55195c2892aae209e60058ce8ab11 |
| 19-agent-orchestration.html | d1382214315bcd50da4aa9fc83de0171c0a2086b93e418e22d2b8d4d98e8227f |
| 20-graph-memory.html | db75af8d4bca294e7bcda5bed818440cefcecb9a92f9072abb097d6541ee4727 |
| 21-mcp-server.html | 38e860c8d103cfdb0e22a4134aa3792640460e4ecd2b56c5c5e030bd7c88f680 |
| 22-acp.html | d0217063db4827a478138dc5b4812623d38a6efcd33684409cab26fb549f25b4 |
| 23-durable-workflows.html | 4d11d9cdb001bafd2dfa6068ea190b9448fe4af5265751fe3ab94f5db441086a |
| 24-agentic-runs.html | a8e8f0a8761cee2bfa4ef388ded53bbe8d7a999e53fad90a603b18188e43337c |
| 25-web-ui-hub.html | df03ea5567d84d63f35270c0ce0c270887c6d6e6266e9f7b0118d9ca11e63e5a |
| 26-web-ui.html | bb3c12a92931de0410e17c4b20b3812982b2b06e317500346869e1fc6fcb7263 |
| 27-http-server-html-ui.html | c09f349729d6e6734d57c33412322559e2aa73ac71138714afaec5b5a308da56 |
| 28-browser-javascript-backend.html | c715ba0e56ca04c1a808b8470873dc6dd1ce2fc7ddfa4103c88a3f8409811f33 |
| 29-rest-api.html | 948e8a3ad587a44df7cb9550c64153a270b7bc35454e1d1daf328632d61f34be |
| 30-rest-web-client.html | 041f60f5b9356349ad9e49281057d8f05de2b3809ee5d8f3a02265154c0e4221 |
| 31-full-stack-development.html | b44c547e1f4ac4ef9c3371b05c8dcc57183a879bbe435360af6e0e0ae8d3714e |
| 32-dotnet-interop.html | 477c8c813389d0d8199c835142ae38479853798d1573e3876b2edbfb4cd4191a |
| 33-device-integration.html | 3f89ec014a910492bcf697070c3612043eecba6fa46a6082c3d519ff82c45ff4 |
| 34-personal-assistant.html | 5d9459733d7cb7d9b4ad1bb79a39e906033b8cd29ead55406461e436e459a045 |
| 35-examples.html | f2e4f6be468d088f13aab835aaf91c66a886ece7b8fd757189aef767307a6711 |
| 36-property-testing.html | 13999f39b7c34d8ba34ab6f8cb7a9864156910e26cb3841fb8a6c9d4130920b0 |
| 37-grammar.html | 75f042c7510a32207f53d30ef71bfe920bb50c9851e9d37bb7f1b95d23cd4e56 |
| 38-appendix.html | 80bef688e919c5784e48cfa605bfcbdb6021ddde0692bc83dd4256d3526ae308 |
| 39-appendix-gpu-billiards.html | 329414f70bf45238c4f39e0d4dcb9c856909f51b10afdc99bf1134554db77955 |
