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
| `softmax_classifier.malda` | 3-class softmax on 2D blobs (`math.matmul`) |
| `svm_linear.malda` | Soft-margin linear SVM (and why XOR needs a hidden layer) |
| `attention_is_all_you_need.malda` | Vaswani et al. 2017 Transformer, then reverse a sequence |
| `microgpt.malda` | Tiny decoder-only GPT (Karpathy port) |
| `embedding_2d.malda` | Hash embeddings projected to 2D + VectorDB neighbors |
| `onnx_inspect.malda` | `new OnnxModel` inspect + forward on `data/identity.onnx` |

See the XOR decision boundary in the browser with
`malda play Examples/Games/xor_decision_boundary.malda`.
Kit roadmap: [`docs/roadmap-neural.md`](../../docs/roadmap-neural.md).

All are seeded with `math.seed` where they use randomness. The student
path arrives here from `Examples/Algorithms/simulated_annealing.malda`.
Tabular Q-learning without the cliff is `Examples/Algorithms/qlearn_grid.malda`.

Catalog fields: [`metadata.json`](metadata.json).
