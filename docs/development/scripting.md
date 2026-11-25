---
title: Scripting
nav_group: Development
nav_order: 4
---

## Decisions and super events

Decisions and super events are JSON expression trees of the form:

```json
{"func": "function_name", "args": [...]}
```

Nested trees are allowed. `InterpreterManager` evaluates them. To add a scriptable function:

1. Add argument types to `InterpreterManager.functions`.
2. Add parameter names to `param_names`; the in-game decision editor shows these.
3. Handle the function in `get_function`.

Variadic functions repeat their last parameter name for extra arguments. The current registry is listed in [Interpreter functions]({% link reference/functions.md %}).

## Building recipes

`building_functions.json` works differently. Values are Godot `Expression` strings, evaluated against `Scripts/Map/TagMatcher.cs`:

```text
has_tag('fuel')
input_amount(0)
input_has_tag(0, 'fuel')
local_pool_amount('power')
```

`TagMatcher.evaluate()` parses and executes the expression; parse or execute failures are pushed as Godot errors and return `false`.

## Significant figures

Skill modifiers are documented in [Important figure skills]({% link reference/important-figure-skills.md %}). The reference distinguishes additive and multiplicative modifiers and notes which ones are applied last.
