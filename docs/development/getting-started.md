---
title: Getting started
nav_group: Development
nav_order: 1
---

Spirits of Steel Rebirth is a Godot **4.7.2 mono/.NET** game using the GL Compatibility renderer. Game code lives in C# (`namespace Sosr`, .NET 8; Android uses .NET 9). Use `godot-mono`, not a standard Godot build. GDScript remains only in `addons/` and `scratch/`.

## Commands

| Purpose | Command |
| --- | --- |
| Open editor | `godot-mono --editor --path .` |
| Run game | `godot-mono --path .` |
| Build C# assembly | `dotnet build` |
| Headless smoke test | `godot-mono --headless --path . --quit-after 300` |
| C# round-trip checks | `godot-mono --headless --path . --script res://scratch/test_port_parity.gd` |
| Save-diff flow checks | `godot-mono --headless --path . res://scratch/test_save_diff_flow.tscn` |
| Settings diff checks | `godot-mono --headless --path . res://scratch/test_settings_manager_diff.tscn` |

There is no code CI, linter, formatter, or formal test suite beyond Godot and the scratch checks. The docs workflow publishes this site; it does not build the game. The smoke test treats a non-zero exit, such as 139, as a crash.

`--script` harnesses cannot see autoloads. Harnesses that need `MapData`, `SettingsManager`, or other autoloads use a `.tscn` scene instead.

## Resource identity

`.uid` files sit beside scripts and shaders. Scenes and `project.godot` reference many resources by `uid://` rather than path. Commit `.uid` files with their source files.

## Export targets

Export presets are in `export_presets.cfg`. The Windows preset still points at Godot 4.6.1 templates in a personal path; check it before relying on that preset.
