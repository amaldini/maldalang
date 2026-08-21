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
| index.html | 851d8834160ece5b35fe7683b80702014b95c03162c2cb0e433a7d70e42ee2f2 |
| 01-introduction.html | a77b84056c70494fcf030a725d6730468792d286f177a41dd4a47071c0669ae4 |
| 02-tools.html | 235f885b0230bd232bccbbfeb5b428cf57f4319c6783b080dd164db1591ba537 |
| 03-lexical-structure.html | 29659babddfbe27030d2e26af07680443081914bd48116f731f63ee8d58f48b4 |
| 04-data-types.html | f7ad0ac24087b1b5170252ca28408796f81fbd1045505d4a566e1395b89c46fd |
| 05-variables.html | a26949e70b649b7de91b4c877c92257d2257dd129fa5c632efe3c71c3510a17d |
| 06-arrays.html | b598119e28050f87bbaac1fc4bcf0395ee0c0cbedc75cd672e332e0749b46891 |
| 07-expressions.html | a02b92b83536b84867824f2b2685474eb37da44fc8c3554dd1b8049c19d51cf9 |
| 08-control-structures.html | 7425713c057e77c207f1afa43f0bdf9c7674862de4bd015c8c5fcbc9200400bb |
| 09-functions.html | d4f68a232e7abb6acc408d55b3f14f672a18fddc9c748936052dfbcadb18a50a |
| 10-prompts.html | 6cda4dc7b6a02e4409f1bcf7d1621a6dde5eef22ea45113d23edc5497d4874a6 |
| 11-classes-objects.html | 0702178b625a5b46cf42cc3d82eb54ae79d75cdea8084cf3217866cf265ed9e3 |
| 12-input-output.html | f987187b3b7f5bcde4726b2e185d0c27944b43a71e3a193f6a1c520cb578fd25 |
| 13-built-in-functions.html | 479ad1387e531fe4f84567e530df84336ed430ed32cd7d5ceb89721510fa4f88 |
| 14-graphs.html | 58afbfc7e886bd1844c42a4afa250013289d040abb90c8d5813e85bfa6750773 |
| 15-vectordb.html | 05a6a95520be77e820f9b95523e0d8cad81141d36942081807d43434a7826ec0 |
| 16-database.html | 1c8702ed18890bf5bedd56dd559f3f7ddb37f6427afb60200b41951dbf31d832 |
| 17-actors.html | 12a9c9ac78dac48aa24466515ca088b4c40060a0ecd8a2c92ad071af0424687d |
| 18-agent-orchestration.html | 0779faa55a48cad66bb03b2e2bd44c978a8be83d5e2ebd592322713383944ee4 |
| 19-graph-memory.html | ade193431c82119345522057f280d8439ad964bd299f53aba9ac78f541aaa4dd |
| 20-mcp-server.html | 8895ef0618ca8e8b0c3f35da1a464cecfaf672d5fd19dfeecd89e0653736a890 |
| 21-acp.html | 89ff4d1018d1278de247cdadca4612c422c7a944ac0ed027ddfd790ed1e64272 |
| 22-durable-workflows.html | f64b9bab1068ee5a4453067fa0ae7df0eee48495ed946be555182f958fd5c368 |
| 23-web-ui-hub.html | 922cf703f0ae17d88f3852ad2b0910354bfabb9cd099174a13a2ed41253a92c8 |
| 24-web-ui.html | 7ab97a01bf67c9555eef4c99f99bdfc4f5b9b01ca96dd3e9aff03168b4ec407b |
| 25-http-server-html-ui.html | e67384fd08ba4572665d683442bec3d0fa38461195b08f2278d67dcbc7238f68 |
| 26-browser-javascript-backend.html | 54c5189c50603d68947b4ee71a45ed9dfdb757c72623740698535c642f7eaff3 |
| 27-rest-api.html | 8f686063dc0fb17410dc863ed73b100a35fd323ad1026722059d39cf1155e1d0 |
| 28-rest-web-client.html | b9e2babacf7400eb5a23dea8213de10e3ebb86b75789824c966dc75fa68e6d4c |
| 29-full-stack-development.html | 114297214f025dc007fe64fc0054c1ded2ff1a7b69c10a5ed3588ce9e6e18ac8 |
| 30-dotnet-interop.html | 9159160ba312c3f84561ef935c53c48e0ba9d0dbb917c7f0bb5c44b80a875adb |
| 31-device-integration.html | 78f61ea699786d382e78e0fb3ed2730a2a817feb46910dc1adf775c5222b30b3 |
| 32-personal-assistant.html | 9dd79858bd6d38d26033fd35f3a60ce37fcb1354c3e3398d14764e0f4054cf2c |
| 33-examples.html | 09213b270de8efb9f27c1d85e0f6f9b65dcde64b77898b70cd133acfa0275f44 |
| 34-property-testing.html | 08624f2e0d04526c837e1a123311e13a3ad687089b9bbc7ace3fe3eb24a1f1fe |
| 35-grammar.html | d6e37109a0562a12de6652a7edcf93230f00b565d7b75ee7027b3678a7261288 |
| 36-appendix.html | f53114822f6378e939ca5618b94808305ee8df8a23792f5d6a29b30f4936aec7 |
