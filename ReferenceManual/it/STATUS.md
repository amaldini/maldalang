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
| index.html | 39be86b65b8d719f548a16475c729718e17763ac4535e0e2cfa95449645ec17b |
| 01-introduction.html | 434fbdde99dce0c545676bdbf8df9d6b33430bce3efea5d13170bbbd4dfc9df8 |
| 02-tools.html | 007a37545caee5917615187e3f2dcfb46269c682ca2511bf194b8ed62a8026eb |
| 03-lexical-structure.html | 58d87f02e579dfcb1daee230764fde6c830767e0c871d29380e7f19374808ec2 |
| 04-data-types.html | 262bf89324115fcdac0815d6f9b186107a93dd7267f3993049cf879964818281 |
| 05-variables.html | 8a49b41e0e1e672a5035d516620930596b8c1cc26b81fa32a71f14345876de57 |
| 06-arrays.html | 2945b8e2712f9ef9fac1ae73939d31fbc0dd49291c9ae5c9e1e8bdbc353b55d3 |
| 07-expressions.html | 19d861e8e086feea17a93f84e0376e70ab582a528ffe70c057febdf723a08062 |
| 08-control-structures.html | dc451f95b68198967ebfb1d2092e60dff3059982716c60af740e047fe178c676 |
| 09-functions.html | 9db1d373eff504dd4a24fd5b940f709f9034cd9394820c9915898de14d395d04 |
| 10-prompts.html | 4ff3f7b9ee5cb0723769845c3409b091366093d5af02c2a6dd8c1452b60a6a7d |
| 11-classes-objects.html | 372adee589e9539c7411cee09c439fb409903d377b2ef13f8bd65882ea1b8ce8 |
| 12-input-output.html | 003858310827054304d68b9c42ae489433129e16a44bf21370e18ee41aa7e002 |
| 13-built-in-functions.html | 26aab62a2865fb1d24d713ccc9d6eed37208288b9c5fe54449aaaf9f7ff8f942 |
| 14-graphs.html | 0e70a60868d23065066932c4632ca32f5663ab1f0481e72f54062d1f29239e43 |
| 15-vectordb.html | 5454e7596503d2a1331eee27eeef90d7f348f73f8408ea18fc9852ef18fcce40 |
| 16-database.html | 3af13d8e1ebd7126116940bdde38eac63050a92bd70752e3f1b26d2584f5a5c0 |
| 17-actors.html | 0274ed3d74dc4dbbfc987ae85bb72f3f58e88ed79bf4f5e2e6e4ac2ed2418e5f |
| 18-agent-orchestration.html | f68a4346664d380c49553b76636a5f44c22c1ad4bc328659f16975c0ee91da81 |
| 19-graph-memory.html | 5bc5c3872c5a1d0f592dfb99fe61ae45bac262c1f5dcf6256e0ca3f7ae7da7ae |
| 20-mcp-server.html | be9e44b0affa7e97ba6d9d4b31dacce0ffbd400e506d304ac1ddd71aa6534966 |
| 21-acp.html | 9cc9aa9279426ab704bbf1aeba6483d7686b6c06bef515052b97a74d11e053f9 |
| 22-durable-workflows.html | b4080c5f4d0ae475dd77cf6153ceaf99cd3b6f5083b1d69078203f70a3f00229 |
| 23-web-ui-hub.html | 7657e2958356e4ad496464ba2dcb081949112daa3eb1af7dd4d5b10f86f2199f |
| 24-web-ui.html | 430e5ca32e3b512d9dea2d5498415bc43d725ae2b53c19a48b7776ba8f0f9731 |
| 25-http-server-html-ui.html | a53b9af8a5385990bd602c0c7f3760921c8959fa5ddbb1b83fc525f162b2d8fa |
| 26-browser-javascript-backend.html | 8329f2e9f58839e6189f3428dbe0d6636d6c1d7242fa17102b51deb9c64e3442 |
| 27-rest-api.html | 38c07499bdad88870d04201cf9764bab169eeb428033f842a534f37fe345baaf |
| 28-rest-web-client.html | 26b513e6d1aa9250e517ae927327263cb47f585f01b190a3212064b2cbe59957 |
| 29-full-stack-development.html | b8fc77d91e762f6ccc83dbadbe10ad05f253b3f8a726711a2e2260b95610b4c9 |
| 30-dotnet-interop.html | 997e1d6f952c7a653c14526c55067d2459249dca655c1b04da973ffe71302987 |
| 31-device-integration.html | 89bfbf3a8ba1df8e56380bc26a64b4db0c624b70f3a3f23ad33d9d7619dde0cd |
| 32-personal-assistant.html | f87459b84a96c4e04e5869fce05bb7ca4be40a208962a4e0056b3fed5493691d |
| 33-examples.html | a7020ff590dfcf3f912a7bf30e3478283cc3ff2913138a7de623b0f762655e86 |
| 34-property-testing.html | 8c97e328151bdeeba2a1c43ae433727e2d0a8b5254fde944cecb0c2b304b500c |
| 35-grammar.html | 1c8c4a17dafa9f17e27e4b104dab1105d4fb5e2a67961d307efe2a423638a667 |
| 36-appendix.html | d19dbc2aaf9a74f94fcfc6c73f4ed024b403744ab46bf130c52fc457ab465293 |
