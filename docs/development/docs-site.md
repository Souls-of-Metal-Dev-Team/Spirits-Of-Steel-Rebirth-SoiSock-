---
title: Docs site
nav_group: Development
nav_order: 5
---

The public documentation is generated from this `docs/` directory with Jekyll and deployed by `.github/workflows/docs.yml`. Pages must be configured to use **GitHub Actions** as the source. The workflow runs on pushes to `main` and `C#Rewrite`, and can also be started manually.

Site settings live in `_config.yml`. `baseurl` is the repository path, including its trailing hyphen:

```yaml
baseurl: "/Spirits-Of-Steel-Rebirth-SoiSock-"
url: "https://souls-of-metal-dev-team.github.io"
```

## Adding a page

Add Markdown under `docs/` with front matter:

```yaml
---
title: Page title
nav_group: Development
nav_order: 6
---
```

The sidebar is generated from `nav_group` and `nav_order`. Valid groups are `Start`, `Development`, `Architecture`, `Reference`, `Features`, and `Planning`. Use the `{% raw %}{% link path/to/page.md %}{% endraw %}` Liquid tag for internal links so the base URL is handled correctly.

## Diagrams

Mermaid is loaded only when a page sets `mermaid: true` in front matter. Use fenced blocks with the `mermaid` language tag. `Excalidraw/` and `.obsidian/` are excluded from the published site.

No Gemfile is committed. The GitHub Actions build uses the Pages Jekyll environment.
