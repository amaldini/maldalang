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
| index.html | 487680c6a6e20fd01fdb6a7a75c69d8305328d6ae2bba3614e7d264394a0d9cf |
| learn.html | e2abeafa6e4c1f7316467b29c3fe765de6334ba36af9c70a98289adf734f84ad |
| 01-introduction.html | d66f1854eabf507472fa480f2f1d89c5211dc2d34cfde73f701a4f40e34e3f85 |
| 02-tools.html | cbc0ef8b7939a745672eae10160daffac16ac0776b5e72e93a8b988e7cf6ddd5 |
| 03-lexical-structure.html | 1386ead306d66a62b4dff862540d9de5ee524804c1cf8cf3cffcc5a2d2645c2b |
| 04-data-types.html | 7bbf2dc045b4b0425bdc69c990b03c57833d61003f32cd038af63aa0039d51b1 |
| 05-variables.html | baee3ecb91efd835ec937027f9799be5445f1fd9295ce184dc4b3c992975692a |
| 06-arrays.html | 40ed7fe770b05930641245587cba2d0d5040b7196c695d87bd7f2b147a1d62ea |
| 07-expressions.html | 3fa417c8cb11106d4df772ef0d17efd7734f5faa6ab350e43796e4d3cf35b154 |
| 08-control-structures.html | 7f09e42c12cd7adc1cfde1e83f8433d48c4cdbe9bb5e1ff7ef96760d3eced4b6 |
| 09-functions.html | 8441f71383640e8ff48e3ec0d2e4ac31655a48010805f8b3dc3bd5bf0fdb9637 |
| 10-prompts.html | c3f4dc4e6dd64fa8b955fa240676b408c9a6b8c8793cd13718bf61045ef21de3 |
| 11-classes-objects.html | 122f4dfd1564f44f1bbf9875644054c80803570565c5e2349cb06beffe06645c |
| 12-input-output.html | 0036170533107a25c3752f8bbfcd0aeb1fea9ca364af01005f41e7dc3f832e66 |
| 13-built-in-functions.html | dbbebdcacb7d5a414050e62c6d535289cd4c0b01fd9fb99a99bc2fb265eb1e15 |
| 14-graphs.html | 755829fdf8b68673d1d992c8689d385e867c36df67182e24f9b47720bb9afc9f |
| 15-vectordb.html | 94bf5f044640afd2526c49d93abb36d7848c6d0d9e9535385aaeb72e7f78dce3 |
| 16-database.html | 3ee153ea5f399d1ca064c92a678b34b31bbc37afa696ddfc15a9f006c24242d0 |
| 17-actors.html | 6b438298bfbb6c2d1a8920e1eb9953cf033949bb8e9d5a65cac7e6d874191e34 |
| 18-agent-orchestration.html | f8fd6e1f52868fda61e72f93438d0c4e6ab9f0b2546e80f3bbd69a1ba0eddbe9 |
| 19-graph-memory.html | 92383e5184ff59ac47e2b2abd681888e82ef5b09e7873e3621c8e0fb6d216900 |
| 20-mcp-server.html | 254a69bfde03ca884d4d10d09cbf6010ee8d78cf57065627a4d2f759d3883dd9 |
| 21-acp.html | ad88b226e24d3e2aeb8200ec65920a0d875a258d57555704f846b9fa73f7d922 |
| 22-durable-workflows.html | abcd81ff6b72039ddf270738b23a2e798492b814a53e7a3b7dbf2be3363618f1 |
| 23-agentic-runs.html | 2f42c41dbb369e8c49739ee248d10ee601b943e5b33bb901a86809213d0bfa34 |
| 24-web-ui-hub.html | 1fa48cced4e93590ef2df2c45b63a0417e0335a9d68e470052926b8d68edff10 |
| 25-web-ui.html | 0b1da8ebbbb68c2684e92db06146ae3aaa21968cbafeabe2fb4aa53bb1cd6ea7 |
| 26-http-server-html-ui.html | 9757249840f6f13d15ff6d238d690ac6632ac492cbf74b4e2f89ee01435909d7 |
| 27-browser-javascript-backend.html | abcdb473018b86cd775a315f7c8d3537e202b576e5672fafc2ff5233bf110f6a |
| 28-rest-api.html | 5fadd52418fc63ac1fdb5630a3718869a99f782ff88cb54b8c879bbfa39c406f |
| 29-rest-web-client.html | 3d7946ad09bb2dce366b68b07188096f2d0c02db9254703b0f52507cd70970fe |
| 30-full-stack-development.html | ce1fa280665ec61c730e62154f02364d73b5a75cd2a19a2f39707d04c1eca624 |
| 31-dotnet-interop.html | 9d3f5db1823331d28a051c3d08c9c501e5d19edce01c3babeb007ab552e1cb58 |
| 32-device-integration.html | e8ade1985fea74fc18b3342b502d9a65031e11a8560eae63bb87e2ab4692aa39 |
| 33-personal-assistant.html | 597f31e08c23a5002a5452ad1615cbfb9b2748de403dd86cc0a6b7390951491b |
| 34-examples.html | 1bb06f9ac7fc6202c02af54e5cef04fcff981711bf637e8472a49466aba0ebb2 |
| 35-property-testing.html | 6959a8762580d0bcdfe525509a0c3e257a468cec92ca2bba85261938d46841df |
| 36-grammar.html | eb17dc4027ca6db7ca2b2c7c2031abb140bcdc9ed018d5d44a0eb07d60962b33 |
| 37-appendix.html | 0a0e5d483c2b830af1fbe9f8212769908aa5b7646c0d62e59141698af98302d1 |
| 38-appendix-gpu-billiards.html | 3a1f78c200fe7a8dec7943e1dc71999c3e76ccd902a9ba9f704d3de71e1cf3a8 |
