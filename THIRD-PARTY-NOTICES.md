# Third-Party Notices

Third-party software included or referenced by UniSphere 360+ UI.

## 1. Lucide Icons (ISC License)

The UI icon set (`Components/UiIcon.razor` + `Components/UiIconPaths.cs`) uses the
Lucide icon geometry (https://lucide.dev).

ISC License — Lucide Icons, Lucide Contributors

> Copyright (c) for portions of Lucide are held by Cole Bemis 2013 and other
> Lucide contributors 2022. Permission to use, copy, modify, and/or distribute
> this software for any purpose with or without fee is hereby granted,
> provided that the above copyright notice and this permission notice appear
> in all copies.
>
> THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
> REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY
> AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
> INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM
> LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR
> OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
> PERFORMANCE OF THIS SOFTWARE.

## 2. Feather Icons (MIT License) — notice shipped by Lucide

Portions of the Lucide icon set are derived from Feather
(https://feathericons.com), which Lucide distributes under this notice:

MIT License — Feather Icons, Cole Bemis 2013

> The MIT License (MIT) Copyright (c) 2013-2016 Cole Bemis
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in
> all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
> THE SOFTWARE.

## 3. Tailwind CSS (MIT License) — build-time tool

Styling is generated with Tailwind CSS (https://tailwindcss.com), used as the
standalone CLI (no npm dependency). Not distributed in the product.

MIT License — Tailwind Labs, Inc.

> Copyright (c) Tailwind Labs, Inc.
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in
> all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
> THE SOFTWARE.

CLI install note (teammates): download the official Tailwind v4 standalone CLI
executable for Windows x64 from
https://github.com/tailwindlabs/tailwindcss/releases (file
`tailwindcss-windows-x64.exe` of the release matching `wwwroot/css/app.css`
header comment), place it at `tools/tailwind/tailwindcss.exe`, and verify its
SHA256 against the `sha256sums.txt` published with that same release.
`tools/tailwind/` is git-ignored; `wwwroot/css/app.generated.css` IS committed
so a machine without the CLI still gets fully styled pages.

## Remaining non-Lucide icons (audit, Phase 1)

Inline `<svg>` (data visualizations — exempt per decision) and the `UiIcon`
component's own shell; no changes made:

- `Components/UiIcon.razor:3` — the `<svg>` shell every icon renders through.
- `Pages/Admin/Analytics.razor:39`, `Pages/Admin/Analytics.razor:105` — chart SVGs.
- `Pages/AdminPanel.razor:71` — chart SVG.
- `Controllers/SearchController.cs:142,160,178,188-214` — server-side result
  strings containing emoji mapped by `UiIconPaths.Legacy` (reported only; not
  touched — server content).
