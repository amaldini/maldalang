# MALDA neural-nets kit

**Status:** N0–N4 and N7 landed; N5–N6 docs/examples only  
**Created:** 2026-09-20  
**Audience:** maintainers extending `math.*`, `nn.*`, and the offline AI_Theory track after Final 1.0

This is the plan that makes MALDA a **good place to explore neural nets** —
Karpathy / from-scratch pedagogy, not PyTorch. Prefer
[`Examples/AI_Theory/`](../Examples/AI_Theory/README.md),
[Reference Manual 14](../ReferenceManual/14-neural-nets.html#signatures) (linear algebra stays in [chapter 13](../ReferenceManual/13-built-in-functions.html#math)),
and the out-of-scope list below for engines that stay out of core.

**Bar today:** `math.dot` / `matmul` / `transpose`, the `nn.*` namespace
(activations, local derivatives, `dense` / `denseBackward`, `mseGrad`,
`softmaxGrad`), the student track through XOR / softmax / attention /
microgpt, a JS decision-boundary playground (`malda play`), hash-embedding
2D projection + VectorDB neighbors, and host-only `new OnnxModel(path)`
inspect + forward. `math.sigmoid` / `tanh` / `softmax` remain and call
the same functions as `nn.sigmoid` / `tanh` / `softmax`. `nn.relu`, `nn.mse`, and
`nn.crossEntropyFromLogits` are only on `nn`.

**Not in scope:** a `tensor` language type, an autograd tape, `Adam` /
DataLoader, GPU training, a general ONNX trainer, product apps or vertical
packs (`AGENTS.md`). Serious inference runtimes stay an **optional pack out
of tree** (`MaldaLang.Compiler/OptionalPack/`). `nn.dense` is one layer
with an explicit activation derivative, not a module zoo.

---

## Guiding principles

1. **Teach the idea, do not hide it.** Named chain-rule gradients stay in
   `xor_neural_net.malda`. `nn.denseBackward` multiplies by the local
   activation derivative and returns `dInput` / `dWeights` / `dBias`; it
   does not build a tape.
2. **Linear algebra stays on `math.*`.** Activations, local derivatives, and
   one dense layer live on `nn.*`. Do not add `tensor.*` or flat aliases
   (`AGENTS.md`). `math.sigmoid` / `tanh` / `softmax` remain. `relu`, `mse`, and
   `crossEntropyFromLogits` are only on `nn`.
3. **Functions beat keywords.** No new parser syntax for layers or tapes
   ([`docs/roadmap-language-constructs.md`](roadmap-language-constructs.md)).
4. **Three backends for `math.*` linear algebra and `nn.*`.** Interpreter,
   C# transpile, and JavaScript all run that slice. VectorDB / LlamaEmbedder
   / `OnnxModel` stay host-only
   ([`docs/spec/backend-capability-matrix.md`](spec/backend-capability-matrix.md)).
5. **Offline examples remain the bar.** AI_Theory programs seed RNG and need
   no API key. Pretrained GGUF / cross-encoder files are optional extras.

---

## Themes and priority

| Rank | Workstream | Status | Why |
|------|------------|--------|-----|
| 0 | **N0** Roadmap file | Landed | One place to track this work (this document) |
| 1 | **N1** `math.*` primitives | Landed | XOR / attention reimplemented matmul and sigmoid |
| 2 | **N2** Track gaps | Landed | Student path jumped from chain rule to a 2-4-1 MLP |
| 3 | **N3** Decision-boundary playground | Landed | Print-only nets hide geometry |
| 4 | **N4** Inspect small models | Landed | Consume-to-learn: embeddings + restricted ONNX |
| 5 | **N5** Didactic data | Partial | Inline arrays; `identity.onnx`; `mnist_digits.malda` (5×5 glyphs). No MNIST download, no ImageNet |
| 6 | **N6** Docs / Web IDE | Landed | Chapter 13 + start-here + catalog |
| 7 | **N7** `nn.*` namespace | Landed | Activations, local derivatives, dense forward/backward |

```text
N0  roadmap file                          (landed)
N1  math.dot / matmul / transpose         (landed)
    math.sigmoid / tanh / softmax
N2  perceptron + softmax_classifier       (landed)
N3  xor_decision_boundary (malda play)    (landed)
N4  embedding_2d + OnnxModel              (landed)
N5  tiny fixtures, not a DataLoader       (partial)
    mnist_digits.malda (5x5, not a download)
N6  RM / start-here / catalog             (landed)
N7  nn.relu / leakyRelu / elu / gelu / silu / softplus
    nn.dRelu / dSigmoid / …               (landed)
    nn.dense / denseBackward
    nn.mseGrad / softmaxGrad
```

---

## N1 — `math.*` linear algebra

| Call | Role |
|------|------|
| `math.dot(a, b)` | 1D vectors, same length, scalar |
| `math.matmul(a, b)` | 2D@2D, 2D@1D, or 1D@2D |
| `math.transpose(matrix)` | 2D nested array |
| `math.sigmoid(x)` / `tanh` | Scalar or elementwise 1D/2D. Also on `nn` |
| `math.softmax(array, temperature?)` | Probability normalization. Also on `nn` |

`nn.relu`, `nn.mse`, and `nn.crossEntropyFromLogits` are only on `nn`.
`math.zeros` / `randn` already existed.
Do not add Glorot init or `oneHot` unless a later slice needs them.

---

## N2 — Student track

After `chain_rule.malda`:

1. `perceptron.malda` — one neuron; AND/OR vs XOR failure
2. `xor_neural_net.malda` — 2-4-1 MLP, named backprop
3. `softmax_classifier.malda` — 3-class 2D softmax

SVM / attention / microgpt stay. Do not mix tabular RL (`sarsa_cliff`) into
the net files.

---

## N3 — See the net

`Examples/Games/xor_decision_boundary.malda` — `malda play`. Canvas grid
colored by the XOR MLP, four training points overlaid. No new `ui.chart`.

---

## N4 — Inspect, do not train

- `Examples/AI_Theory/embedding_2d.malda` — `embedHash` + two projection
  axes + VectorDB neighbors (offline; LlamaEmbedder optional later).
- `new OnnxModel(path)` — `inputs()` / `outputs()` / `run(feeds)`. Host-only.
  C# transpile auto-includes `Microsoft.ML.OnnxRuntime` when the source
  mentions `OnnxModel` (same idea as `LlamaEmbedder` → LLamaSharp).
  Example: `Examples/AI_Theory/onnx_inspect.malda` on
  `Examples/AI_Theory/data/identity.onnx`, then the optional
  `malda memory download-rerank` cross-encoder.

---

## Out of scope (keep out of core)

- Language `tensor` / autograd tape / Adam / DataLoader
- GPU, distributed training, ONNX training loops
- Downloading MNIST / ImageNet in the OSS repo (`mnist_digits.malda` is ten 5×5 glyphs on the chapter 14 step)
- A second plotting namespace

---

## N7 — `nn.*`

`math.dot` / `matmul` / `transpose` stay the linear-algebra primitives.
`nn` groups the neural-net helpers. `math.sigmoid` / `tanh` / `softmax` remain
and call the same functions. `relu`, `mse`, and `crossEntropyFromLogits` are only on `nn`.

| Call | Role |
|------|------|
| `nn.relu` / `sigmoid` / `tanh` / `leakyRelu` / `elu` / `gelu` / `silu` / `softplus` | Scalar or elementwise 1D/2D. `relu` is only on `nn`. `leakyRelu` alpha defaults to `0.01`; `elu` alpha defaults to `1` |
| `nn.mse(pred, target)` | Mean squared error. Only on `nn` |
| `nn.crossEntropyFromLogits(logits, targetIndex)` | Classification loss from logits and a class index. Only on `nn` |
| `nn.dRelu` / `dLeakyRelu` / `dElu` / `dGelu` / `dSilu` / `dSoftplus` / `dSigmoid` / `dTanh` | Derivative with respect to the pre-activation |
| `nn.dense(x, weights, bias?, activation?)` | `{ pre, out }`. Weights are `[in, out]` |
| `nn.denseBackward(x, weights, upstream, activation?, pre?)` | `{ dInput, dWeights, dBias }`. Pass forward `pre` when the activation is not linear |
| `nn.mseGrad(pred, target)` | Elementwise `pred - target` (gradient of `1/2 (p-t)^2`) |
| `nn.softmaxGrad(logits, target)` | `softmax(logits)` minus a class index or a same-length vector |

`Examples/AI_Theory/xor_neural_net.malda` keeps the named chain rule.
`Examples/AI_Theory/nn_dense.malda` trains the same XOR net through `nn.dense`.

---

## Related

- Games kit (JS canvas): [`docs/roadmap-games.md`](roadmap-games.md)
- Built-in checklist: [`AGENTS.md`](../AGENTS.md)
- Backend matrix: [`docs/spec/backend-capability-matrix.md`](spec/backend-capability-matrix.md)
