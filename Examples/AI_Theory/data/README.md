Tiny ONNX Identity graph (`X` → `Y`, float32 2×2) for
`Examples/AI_Theory/onnx_inspect.malda`.

Regenerate:

```bash
python -c "from onnx import helper, TensorProto, save; X = helper.make_tensor_value_info('X', TensorProto.FLOAT, [2, 2]); Y = helper.make_tensor_value_info('Y', TensorProto.FLOAT, [2, 2]); node = helper.make_node('Identity', ['X'], ['Y']); graph = helper.make_graph([node], 'identity', [X], [Y]); model = helper.make_model(graph, opset_imports=[helper.make_opsetid('', 13)], ir_version=8); save(model, 'identity.onnx')"
```
