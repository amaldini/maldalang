# Testing examples

Unit tests (`*.test.malda`) and property-testing samples for `malda test`.

## Run

```bash
# Single file under the interpreter
malda Examples/Testing/unit_test_basics.test.malda

# Project-style test run (from a scaffold or when a test layout is present)
malda test
malda test --format ci
```

## Notes

- Prefer `function` in new tests.
- Property cases may skip JS-incapable features; see `property_js_capability_skip.malda`.
- Catalog: [`metadata.json`](metadata.json).
