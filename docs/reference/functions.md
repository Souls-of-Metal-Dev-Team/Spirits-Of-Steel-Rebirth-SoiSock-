---
title: Interpreter functions
nav_group: Reference
nav_order: 2
---

Function registry and parameter names come from `Managers/InterpreterManager.cs`. Variadic functions repeat their last parameter name for extra arguments. See [Scripting]({% link development/scripting.md %}) for how these are added and evaluated.

| Function | Parameters |
| --- | --- |
| `return` | `value` |
| `and` | `condition` |
| `not` | `condition` |
| `or` | `condition` |
| `xor` | `condition_a`, `condition_b` |
| `eq` | `left`, `right` |
| `gt` | `left`, `right` |
| `lt` | `left`, `right` |
| `all` | `condition` |
| `any` | `condition` |
| `probability` | `chance` |
| `get` | `country`, `attribute` |
| `set_country_attr` | `attribute`, `value` |
| `add_country_attr` | `attribute`, `amount` |
| `add` | `a`, `b` |
| `is_at_war` | `country_a`, `country_b` |
| `army_level_up` | none |
| `build_factory` | `amount` |
| `declare_war` | `attacker`, `defender` |
| `release` | `country` |
| `play_as` | `country` |
| `annex` | `annexer`, `annexee` |
| `add_ideology_drift` | `country`, `ideology`, `drift` |
| `make_puppet` | `puppeteer`, `puppet` |
| `release_puppet` | `puppeteer`, `puppet` |
| `has_pids` | `owner`, `pid` |
| `add_plans` | `plan` |
| `remove_plan` | `plan` |
| `event` | `event` |
| `schedule_event` | `days`, `event` |
| `invite` | `inviter`, `invitee` |
| `change_province_types` | `pids`, `type`, `country_name` |
| `set_fig_attr` | `figure`, `attribute`, `value` |
