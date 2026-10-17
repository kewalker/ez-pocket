# ez-pocket landing page

Static, dependency-free public landing page. `dist/` contains the deployable site, including copies of the screenshots in `docs/media/`.

Preview from this directory with `node preview.mjs`, then open the printed local URL. The preview binds only to this computer.

The public site is hosted at https://kewalker.github.io/ez-pocket/. `.github/workflows/pages.yml` publishes `website/dist/` automatically when that directory or the workflow changes on `main`. It can also be run manually from GitHub Actions. Repository Settings → Pages must use **GitHub Actions** as its publishing source.

Use relative asset links so the site works below the `/ez-pocket/` project path. Update the canonical URL, Open Graph metadata, sitemap, and robots file together if the public address changes. Refresh asset copies when the source screenshots change, and keep screenshot version captions accurate.
