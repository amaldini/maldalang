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
| index.html | 8ce115d61531babd57a9102222d3b43038e6f8573349d047a259dbd645048cf7 |
| 01-introduction.html | ab588f864a2358ffe63ac40d47ee67b6a18812e3fadd54a91021a4e2343a47ca |
| 02-tools.html | a9f4eb96c0e5acc36d16298efde6f050ecddf374f831e4222c5856a456010e15 |
| 03-lexical-structure.html | 447eb455b33310099a9b3135664625aa74493e17cfa3fb4e415a7546b549d816 |
| 04-data-types.html | f5c6f8d3059e8952e195d3ea6d1ec6fb5e6e983b6d2a8bc4a154f98e84bbad2f |
| 05-variables.html | d063ef64745422680949e1303679d59871663e15cb38984d129bca072e1a8294 |
| 06-arrays.html | 35e85d2d78faed7892e02184fe28d80ee994109b2b320baeec2a2b2a23f37eb7 |
| 07-expressions.html | 1a3d8e9c91853d4bcda8d035cbab93d24611da5fb6bda6220bf5c690d93909e2 |
| 08-control-structures.html | 2cef3a7b19b827ac8992eabd782b26647af6d6710f163896628805486a3794cc |
| 09-functions.html | 81ff21a546ef5134a180e154ccf57e18065cc166dd8eb8ab7f04f604a68bb22c |
| 10-classes-objects.html | e6b07ed0721710619a89cbba1df62365113cdc43e6144095e1dfbc3e13587067 |
| 11-input-output.html | 03b055278bcd2fd020cbd081426a881c1d85e8b0a0df7eb751e4810238940dd8 |
| 12-built-in-functions.html | d81c6448579d7b276d907c3efac633b790055910d7cd6780cef6ae4faf8de62c |
| 13-graphs.html | 2c6ed8fbcbe982da882e98b15e8f51f6365b2a1847250414097d32c8eb1e6605 |
| 14-vectordb.html | 9bd35077f98938cae67c2caba8d69cfe779b41491ce8a8234e51009d446016ae |
| 15-database.html | 08ac081a483ae252932b95fabac86699a22a44694bd5cbbbe0c967ecae914b5d |
| 16-actors.html | 68aa93c4831a34690ecd77926c004516de678f9628f56f20df63770de5ffe2eb |
| 17-agent-orchestration.html | 7f27113bbcc4418d56f480a815414184931dca52abcee87389df512c7d089d20 |
| 18-graph-memory.html | 3acd9d7eab8cfb23ad387449d9eedc17df7d0c65e8b1f74852055e524a51b18d |
| 19-mcp-server.html | d972ff9dc741efded326d8bb54887f2c6b1fc3896a690ce6646734ab67af8f24 |
| 20-acp.html | e987aa2cd36df9fb544c6cfc6ce4aae8ef829b29a9c12e43fc296a656b7bb174 |
| 21-durable-workflows.html | 1c5b9ff5001b02de2f0f0707ba1d04e8725a6bdf8f31af78197ef915ebe8001b |
| 22-web-ui-hub.html | 4664913b3563df539f19c7b2e5e65dbd6a54ded7ad5d86530a51074bf1211c3c |
| 23-web-ui.html | 10d64ce14cba6411cd3e22caba4a0831907370808538387905761c3e9ebb9713 |
| 24-http-server-html-ui.html | 0941e6036e5bd0c1d2a247d68c498bea06d0548e7019b9af35d731ac0b9c40a8 |
| 25-browser-javascript-backend.html | 02a1b78be2df97d3e0f3c16c4eee6707f94a61a8f2979d75bdcfead84e3f4772 |
| 26-rest-api.html | c52a6e46895b97097f10ec66b382227e09af1fc4fe08e7673d92e679bf5812ab |
| 27-rest-web-client.html | 542117fdf945e0c5612b18f8464c934945ae9a5dfcc908178934f3e9ad822999 |
| 28-full-stack-development.html | d480d7a9ed9b96e5d2beffd6d72b4af8b809888821d56e6074f019b42ac83c56 |
| 29-dotnet-interop.html | edf30db51ee48615185a9540b81880c7b4959a8e53625fc7926a18f989ab66e6 |
| 30-device-integration.html | 9430f645f95fa9bad64cc1e1d80a0da3f3f3aea3e43f113c0036a4b0dba7b0bd |
| 31-personal-assistant.html | cace26e2525b8531c70feb1e952e85484f3607658e9169eef4358b072f041d74 |
| 32-examples.html | 9220aa684af3c9054d1f734718361634492c31cb4fab271e202c247c64006779 |
| 33-property-testing.html | e59b19522772ca4f49b2d0a609daa0cbaabc13706b6603311242c0eb5eaffd19 |
| 34-grammar.html | 1d5a9db83f224cc0caba547e87cdd5921e343722c1105cd5c22ab8b24b4457eb |
| 35-appendix.html | 8a7441f78c68607e55653ab4d7d35e9da1e4cbde7f17b1811a50a468fd64d8fa |
