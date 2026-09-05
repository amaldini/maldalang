# Algorithms examples

A short student track after the CS101 extras in `Examples/Basics/`
(lists, dictionaries, recursion). These files teach **ideas in MALDA**,
not a complete algorithms encyclopedia.

| File | Idea |
|------|------|
| `binary_search.malda` | Loop invariant on a sorted array |
| `merge_sort.malda` | One O(n log n) sort, written out |
| `bfs_dfs.malda` | Hand-rolled walks, then the `graph` builtin |
| `knapsack.malda` | One dynamic-programming table |
| `union_find.malda` | Disjoint sets / components |
| `qlearn_grid.malda` | Tabular reinforcement learning |
| `simulated_annealing.malda` | Non-gradient search (tiny TSP) |

All are offline and (where random) seeded with `math.seed`. After the
annealing sample, the path continues to
`Examples/AI_LLM/xor_neural_net.malda` (gradient learning).

Builtin graph algorithms stay in [`Examples/Graphs/`](../Graphs/).
Catalog fields: [`metadata.json`](metadata.json).
