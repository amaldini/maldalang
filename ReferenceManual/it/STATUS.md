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
| index.html | 6559c7d052f4f85e9ae8124ee583d7badfd4602a02b72bce94bbb484a46f3117 |
| learn.html | ff42e5b010f25b4df257923d10302b2897212bc491b175b2f5487fe486ad729d |
| 01-introduction.html | 3a66fa9ca0487252162cac72fdaa29ca594005851912e9c1926a64ab4178fa9a |
| 02-tools.html | f2d14e20de2494672eee7b843df8598ac9610cb26712d706108ad9f3563860b8 |
| 03-lexical-structure.html | 1639ea4573cfaf3329c1584a06cede3b9a90738ee05bd185f3cb27a6a226370c |
| 04-data-types.html | 0c835dc28a8a0862abc8ce8691721bc454a6fedc03e35b515c1c07aa4528bfa9 |
| 05-variables.html | baee3ecb91efd835ec937027f9799be5445f1fd9295ce184dc4b3c992975692a |
| 06-arrays.html | 40ed7fe770b05930641245587cba2d0d5040b7196c695d87bd7f2b147a1d62ea |
| 07-expressions.html | 0dfb8786d202bb3738f2f6ba9e846dd6bed4a185497c01492c02b26057365458 |
| 08-control-structures.html | 7cfb9b0528706be7177ba4b8c14fa15cf2aa0432df0b86e289677d5e4ea8da3b |
| 09-functions.html | c15fec0c9a53e6fb0fd58e54ca67ae8e83e81f65074ada3ea8c36093b4b92625 |
| 10-prompts.html | 92f488dd27cd134278a33ff0afd59a08b64b099d6fef5f860f910d1348b2829a |
| 11-classes-objects.html | 24708b4378930370e9fa3e7f877a78a151164e4a91813dd6ceaf70a68121eb54 |
| 12-input-output.html | 08173ba3a48ba0c257a30746e20047c6ce9627c3895a059a70826809984bf352 |
| 13-built-in-functions.html | 269edb41633b602d40268a04e662c25765f45a5078f09c4e07d2477583789c23 |
| 14-neural-nets.html | c9d75e10c641130539e55829b2221d3f41c542f31f04548e47a84c27832541a8 |
| 15-graphs.html | 9d235a138e006297e6a5557f150bc851909afbb9bb750d45781de0ff10b610fb |
| 16-vectordb.html | 4834a01ec161a1e9b9d89caa38b1e50be0e36cc768a135ada20c85cfa7c39219 |
| 17-database.html | 5601ea7263349b01562903bf2847dd3182356bfb3a00033f43543ebfbcdb27f3 |
| 18-actors.html | c957b9e0aa57e329b57a1c2971b504f103b37020a6505f921c9990f2474fe6a6 |
| 19-agent-orchestration.html | 33f51a8d0ecec0eeb2b2c4d0d1dab2c8f64631a27d7b75d53191fb2f4ec080d1 |
| 20-graph-memory.html | 8c80b1f5ba909126fbccb6ee33f9d8c665f5a54abb87e4ccd83382cccae2a43e |
| 21-mcp-server.html | 68b62eb50541680b259282ccbc7cf7e94b08f2ae44da45ee6977aef015adb165 |
| 22-acp.html | f38f2f60d4807c4e3595e9183d18a1c8e8923e9ce17861655a3bc6e143ff38a5 |
| 23-durable-workflows.html | d3fdf7a003734ea44d0add1c088d8c5d422276b50e77e75dfe778d6e3e0c286a |
| 24-agentic-runs.html | f78640c5f34993a9de6d7891f6c5d9f03b430e4c2767a31be845ff127fd781fc |
| 25-web-ui-hub.html | 695f8c14a9e3e706e16ac34915b03c1e882a586dc75dc7c3561e73fbf998972d |
| 26-web-ui.html | eb8eb17b5add4907f486931666dba5cc5b74e8e7de7d9ab64178506c57adcd87 |
| 27-http-server-html-ui.html | c3d29a5394cb2811a0441747effa65ce04253c47833e36f3ea062c0c9aa6d09e |
| 28-browser-javascript-backend.html | c6f15d796e3ef6408219c6fb42756d15a13f4f9897624c83dcf0ece3f11eab0f |
| 29-rest-api.html | d75539994d8d67364612ccd5b03839a002a9d2416fd1139630f12b31f191538e |
| 30-rest-web-client.html | 2957b02afbddee2fba7c05eafd7b07b37629aaffdd1d41e90bc5a04a97ba0d1c |
| 31-full-stack-development.html | a91ccb23351ad4b26895f9aab8adbc7ef9d9688afd34ef108ca429bdd40eedfa |
| 32-dotnet-interop.html | f16ea5e1f7dd1d432aa7478d2f2f0110dc64eadc0b69c4f5f99f74696b4f164a |
| 33-device-integration.html | af1413684a5a3fa2f233418ab3cea214a7d2a22352b394fbef6c16749cb1fbfb |
| 34-personal-assistant.html | c4b59fe59c276a1c0e78ff01aa531a6f902873a761a4066ccc4f9749173c6918 |
| 35-examples.html | 37b3483a4a7707103a3e55d73c4805fa39343fcaf99f65cafcf91c12d253f54e |
| 36-property-testing.html | ab25266f7b53dd30baec62203b9ede62abe4a70c560ab979e3b19d810c84e6ba |
| 37-grammar.html | 4a16b0ce9d279a7574aed99799aa5fc2b94f97e5e98a462d2696d07bf2e7b7a0 |
| 38-appendix.html | 70d9ef7e021477114f1e189971cb574eb0ea25cf9cae3c67c3d0a3c077321d8d |
| 39-appendix-gpu-billiards.html | 623ebcd8bfb381dd6230e8ed520220a66c27b491b9da427c9d7bbacef8c95b99 |
