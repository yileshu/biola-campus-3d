# Biola Campus 3D (Unity 6 + Cesium for Unity, Web build)

Unofficial, fan-made, walkable 3D recreation of Biola University (La Mirada, CA) built with
Unity 6, Cesium for Unity and Google Photorealistic 3D Tiles, playable in the browser.

> Work in progress — see the full README in a later milestone.

## Secrets
* Copy `Assets/Config/secrets.example.json` to `Assets/Config/secrets.json` and put your
  Map Tiles API key in it. That file (and `.env`) is git-ignored.
* CI reads the key from the `GOOGLE_MAPS_API_KEY` repository secret.
* Run `Tools/git/install-hooks.sh` once to add a pre-commit secret scan.
