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
| index.html | 7b9ad14a930ee0bc061c565fd87ac4471bfe2edb8173415bc8b67ef9c2c0d1e9 |
| 01-introduction.html | 08bf98340ea7a1c5102c8c7979c677c072374e7c01721ae36c6236d2613a8eaa |
| 02-tools.html | 583bc63d534d7017759bc3d2e19ccf37dd1a421c08bcd55b29730025f60a57c7 |
| 03-lexical-structure.html | 930c84e1318bd86553923980c68be5ceeeb3289d79805d8a94d5fe0b8091310e |
| 04-data-types.html | f1a80eab7afc2b0ba2e2404d2a3d6ab33210da6ad848866f6818d7288ff632a3 |
| 05-variables.html | f87fa8e1367176398b77d1ce78663f1ff99a7b4068fa39e40ed274c678103185 |
| 06-arrays.html | e421ca97dac2c9e85f545975a622ebf07e722894be86eb4f85a84beb18443211 |
| 07-expressions.html | 1cea6288943fb7ec54045884c357b0c43e32e8ae9c9d24c53e1da2aed0f047d6 |
| 08-control-structures.html | 09946fc2eb81941f63e2ba151977ade09be7c13793b937155e7ee7426edaf10b |
| 09-functions.html | 70ba32b093fb4c648e7d6b06c7cbcbb9193be41c14ce5e5b93f8fd93d1475955 |
| 10-classes-objects.html | c55e4dd5a7cb8a06ab9842ad8d31c451964646f3d3750d68f93b2fc3acd6c0c6 |
| 11-input-output.html | 6f15d087518adf45d63d64975fcdaaaa2b2f87b096ecf2ee5e2648b227ce8120 |
| 12-built-in-functions.html | bf63391229e3d58eb54d8d8fdea26f8073b4332f6a0566e2877866713ebabda5 |
| 13-graphs.html | 20f32c3a1542a71f4448f0f40540b1bb56819ac5eb5e8991c4eda16002680b21 |
| 14-vectordb.html | 69684ad49b2905d2c6c93d7550d7a0e60c902318c111252baa23fea32f2714e4 |
| 15-database.html | 23af11aa69cfe3011ab3a8476a388f03eee966b5cfcc8aa9435848574fed2eab |
| 16-actors.html | e8c230189a95caa1a9c601c3dd395161830d519a24229d28c25167693483d979 |
| 17-agent-orchestration.html | 965467ae2099819a2ec1d6cb2eed3f456200380a256dbddd4e501a366296c153 |
| 18-graph-memory.html | d2de2a79067ae5dc28c18fa86f3507c4f5f3b4e07c7437e1a7b39ca2af88c661 |
| 19-mcp-server.html | 2f4ed73e8b23ad0a248b3656486a652b0fe221ea465a99bda15f0c8067f41f6d |
| 20-acp.html | 1f051b0ecc104416dab265302a1fe9457484533594eb2b1d33e871047bb003bb |
| 21-durable-workflows.html | 3e8d08a2036586fd2fac238f05d073f6c9ed1fd6d46d0139c769b7283333bbd4 |
| 22-web-ui-hub.html | 42dcdfa9f9ef82050d960cacdb1ca7d0e4cc8f90fecec0c1b14f64425516446d |
| 23-web-ui.html | ca8b9a89e50b20d62391dd4eb4fef84d355410a04db6429237ef343d45dc8296 |
| 24-http-server-html-ui.html | 7964ee4e07bf998ee1d19e3725cc903e6c650a74b76675ef7667cace13d43fe1 |
| 25-browser-javascript-backend.html | f650ca4c928da2f71511e80c09707c83b05095e7ead737bd1574a0b59233c7da |
| 26-rest-api.html | 08edc0e12a3a971fe2b2b6759668c1293fc4b2b1142b1c67994a9f5b417c3973 |
| 27-rest-web-client.html | a28eb805671b5a0151f399d799a183eb893458e47d598dbdb2da26179dc1a9d1 |
| 28-full-stack-development.html | 012e54de455ab00e045ca71f58d58f02f338c0e65685b6bb0cbe99b79de7b74a |
| 29-dotnet-interop.html | 80b51fe3498bf65e63ff74be29df1cdff25c77c3e5b52cb433d67500ec8eba28 |
| 30-device-integration.html | bd4636c53a56b82a5aa9cf146ba0fe5de073bb719281bc7247e73fc4db72e870 |
| 31-personal-assistant.html | 3bb820c1b1900115165fe9a92890b92142f029fee1d84aa9241fb65faab3b8d0 |
| 32-examples.html | 9a5546cc90e5934654b27cdb827d020f992822b90fd1a47ad4f2560bd5d1e836 |
| 33-property-testing.html | c9c8001f834e4213dac8d7119319df694ddfdee33c84322b80bbd45b45d4296c |
| 34-grammar.html | 0dd7a99534bcf89170c4206b046cfaf00444f56a069cc01cdbf4d4f1ccc75add |
| 35-appendix.html | 5ba96194fca96e6af501bbcd9e5e8146fde3de10791d9abb9d6235f9d311ab66 |
