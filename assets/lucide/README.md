# Lucide SVG assets

These files are copied unchanged from the official `lucide-static` npm package,
version `1.34.0`. Only the icons used by ZSnaper are included and embedded into
the application assembly at build time.

- Source: https://www.npmjs.com/package/lucide-static/v/1.34.0
- License: ISC; see `LICENSE.lucide.txt`
- Default geometry: 24 x 24 viewBox, no fill, 2-unit round stroke

When updating Lucide, replace the selected SVG files from one pinned
`lucide-static` release together, update the version above, and keep the SVG
markup unchanged. Runtime color is provided by `LucideRenderer` by replacing
the SVG `currentColor` value before rendering.
