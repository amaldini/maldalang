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
| index.html | 83a4c355c8890dad3b32f6d0ccef119c0c287cf5f3df77f06c220355d52f686f |
| 01-introduction.html | 380e73acf96f188002a42a2255e5a7de44dce2f810597a84d4efdf6d8e74ad64 |
| 02-tools.html | 56a689e13f032875727944b58a549977540ea24ff78b43c279d413ac0f56a59d |
| 03-lexical-structure.html | 58deeb3e1982982c62a5234ec09b3da81becffcd7bc0654055d02a922403f29c |
| 04-data-types.html | 705129432491ce15d6a30e9d4b64ab31b0164bdc3ce1706344f7d65efae1a38f |
| 05-variables.html | 8a49b41e0e1e672a5035d516620930596b8c1cc26b81fa32a71f14345876de57 |
| 06-arrays.html | fe8d15d58158469ec4c1c48bb710249d4b36f2b8b34a47f925f3ff4dca6216a5 |
| 07-expressions.html | 6d330a16fd592cd75edb6284e45eb2cd74eefaf09a001f9fbd4239a902551c35 |
| 08-control-structures.html | dc451f95b68198967ebfb1d2092e60dff3059982716c60af740e047fe178c676 |
| 09-functions.html | 542ec19acccc0ef09cc0b876c23405b0cccd65ddaa41b1bf1763b85a7a8a4012 |
| 10-classes-objects.html | 7db8c27ede0cde50a20f66d9413759fd0813de22f5731f0bbd1dfe4bce0068c4 |
| 11-input-output.html | 9b3a9727d810dc02c0788365b452868d5dc7f54b22f976a5a4fa6823f82fdcf7 |
| 12-built-in-functions.html | f0f45b2eaa27b494d16f8384dec060855bd2ea182657808bcbcfb8182b4ebb5e |
| 13-graphs.html | e66d80e04abdb86a2a7ae4635bb6629087b1042e4f69d6fe0205a0efdb5d5cf4 |
| 14-vectordb.html | 46050b5c3af274627353bc2450fd448520d07fcaee3461cb34be79ff4b65f6f1 |
| 15-database.html | 32403df196d649fce3e3e85fd27a5999e599ed866992a131c125fb4c25881d59 |
| 16-actors.html | ba45362983600fa4423ee3c2fe0144b4f41a6810291f0644050c8659c3cecd78 |
| 17-agent-orchestration.html | 741b64a525fa3163b8340befd77b3b01b27770810a22fcb698eb02341444f0c2 |
| 18-graph-memory.html | ed6b10060f484bb532362b71afa551a310742ec9fce6cfbeaaaa29e564a8f05d |
| 19-mcp-server.html | 1f17efd8a7dfaeb41f206afeb48ff20b4dface25fd02b20bf642a5a813739fb4 |
| 20-acp.html | fc58d4ad214adfbed1bedd5affbd1f0c58577542d2f5be623ccff60eb5b71066 |
| 21-durable-workflows.html | 39f4ecee0b23c89c6e4609d44ef8f10f9a5abd73ad9485addf5192a5ac721e90 |
| 22-web-ui-hub.html | b47857010783037de0bc04af222150093299a25ad27d784d1852f3a5c36795a7 |
| 23-web-ui.html | e24ba32898d6e8c2c0afbbafcad244121e2bff13b7b0f118104ec42e013ad566 |
| 24-http-server-html-ui.html | bc1034c75a6747adb25c60b5042a9850d4dbeee8c5ffc913f60e781c5c47c57b |
| 25-browser-javascript-backend.html | 7ddcd96b352b7cdc40ed100389cf5e3e65122718ff818687e2bb538595bc914e |
| 26-rest-api.html | 50ee7475f45969fdd157fa716a0ffc6257e6fb682cbd50579b268eaffa2d8bcc |
| 27-rest-web-client.html | 7a2b707252723da15b6bcf76307de52e21d178d8cb1a73f843e88e70d25d10b8 |
| 28-full-stack-development.html | 43a42d7050c3a71a18735bd120ca3b5bd63c558292c84840d45f3765617dbc8c |
| 29-dotnet-interop.html | b57662a931a85c4c80c2f4b225d0b8bbe928f75c1182868ef2d69e9c4b3d48d9 |
| 30-device-integration.html | b1ab706b8eb6f2cdb4d6cb7c8afd8a857216784040e120bb3e62e8e7054e7295 |
| 31-personal-assistant.html | 6f083e05ef8850ecc668678e087c067324abae481f5869a7070d0c69906f84a8 |
| 32-examples.html | 69c3fa44abff9eb91ca95d3a7129d6b9a154708dc2ea70267ddc0f5c621c0e8b |
| 33-property-testing.html | f89a065930603138c35d81d34c607fb3168e02ad508941236b6167e39743fe8f |
| 34-grammar.html | a240d3707842f679e95ad0c8756ef632826ea6b34902bfabd6082b57fc14058f |
| 35-appendix.html | b319728cb3d99471f80edd86d01d3dd49db97c2041a44396b7f535a2c080f3ef |
