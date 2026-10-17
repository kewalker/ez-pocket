# ez-pocket landing page

Static, dependency-free public landing page. `dist/` contains the deployable site, including copies of the screenshots in `docs/media/`.

Preview from this directory with `node preview.mjs`, then open the printed local URL. The preview binds only to this computer.

Hosting identity is in `.openai/hosting.json`. This directory is the reviewable source in the app repository. The ignored root `site/` directory is the separate Sites publishing checkout. Copy this directory's contents into that checkout before publishing with the Sites source workflow; preserve the existing project ID and the checkout's Git metadata. Refresh the asset copies when the source screenshots change. Keep screenshot version captions accurate.
