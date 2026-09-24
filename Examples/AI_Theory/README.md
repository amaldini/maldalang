# AI theory examples

From-scratch machine-learning programs. Offline, no API key, no agent
runtime. These teach the ideas behind models — not how MALDA calls one.

Applied LLM clients and conversations stay in [`Examples/AI_LLM/`](../AI_LLM/).
Agents and tools stay in [`Examples/Agents/`](../Agents/).

| File | Idea |
|------|------|
| `sarsa_cliff.malda` | SARSA vs Q-learning on a cliff (on-policy vs off-policy) |
| `chain_rule.malda` | d/dx [f(g(x))] as a numerical vs analytic check |
| `perceptron.malda` | One neuron: AND/OR vs XOR |
| `xor_neural_net.malda` | 2-4-1 MLP, named chain-rule gradients, XOR |
| `nn_dense.malda` | Same XOR net via `nn.dense` / `nn.denseBackward` |
| `softmax_classifier.malda` | 3-class softmax on 2D blobs (`math.matmul`) |
| `mnist_digits.malda` | Ten 5×5 glyphs, chapter 14 MLP (ReLU hidden, 10 linear logits) |
| `gradcheck_dense.malda` | Central difference vs `nn.denseBackward` on one ReLU layer |
| `init_scale.malda` | Uniform vs Xavier vs He on one forward pass |
| `glyph_holdout.malda` | Same glyphs; two flipped pixels, train perfect and holdout not |
| `momentum_valley.malda` | SGD vs momentum on a steep quadratic |
| `rnn_delay.malda` | Vanishing sigmoid recurrence, then a tanh net that recalls a delayed bit |
| `conv_stroke.malda` | One shared 3×3 kernel on vertical and horizontal dashes |
| `residual_dropout.malda` | Residual skip vs a deep sigmoid stack, then a train-time dropout mask |
| `embed_row.malda` | One embedding row receives the gradient; the other rows stay put |
| `next_char.malda` | Bigram on a repeated inline word via `nn.dense` and a row scatter |
| `layer_norm.malda` | Mean and standard deviation keep a deep product near 1; grad check on the scale |
| `attention_step.malda` | One head, `QK^T / sqrt(d)`, then a causal mask that hides the future |
| `svm_linear.malda` | Soft-margin linear SVM (and why XOR needs a hidden layer) |
| `attention_is_all_you_need.malda` | Vaswani et al. 2017 Transformer, then reverse a sequence |
| `microgpt.malda` | Tiny decoder-only GPT (Karpathy port) |
| `embedding_2d.malda` | Hash embeddings projected to 2D + VectorDB neighbors |
| `onnx_inspect.malda` | `new OnnxModel` inspect + forward on `data/identity.onnx` |

See the XOR decision boundary in the browser with
`malda play Examples/Games/xor_decision_boundary.malda`, and the three
softmax regions with
`malda play Examples/Games/softmax_decision_boundary.malda`.
Kit roadmap: [`docs/roadmap-neural.md`](../../docs/roadmap-neural.md).

All are seeded with `math.seed` where they use randomness. The student
path arrives here from `Examples/Algorithms/simulated_annealing.malda`.
Tabular Q-learning without the cliff is `Examples/Algorithms/qlearn_grid.malda`.

Catalog fields: [`metadata.json`](metadata.json).
