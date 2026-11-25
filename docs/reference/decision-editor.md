---
title: Decision editor
nav_group: Reference
nav_order: 1
---

## Controls

- **Shift + middle-click:** delete a decision.
- **Right-click:** connect decisions.

## Expression format

Decisions and super events are JSON expression trees:

```json
{"func": "function_name", "args": [...]}
```

`args` may contain nested expression trees. `InterpreterManager` evaluates them; available functions and parameter names are listed in [Interpreter functions]({% link reference/functions.md %}). Adding a function requires changes to `functions`, `param_names`, and `get_function`; see [Scripting]({% link development/scripting.md %}).
