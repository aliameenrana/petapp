# PetApp

A 3D pet game prototype for iOS, built entirely in Unity: a tab bar (Home, Mini-games, Feed), one coin balance shared by every screen and saved across restarts, and mini-games that are not in the app install. Full design: [PetApp-Architecture.pdf](PetApp-Architecture.pdf).

## Project structure and key packages

| Folder | What it is |
|---|---|
| `PetAPP/` | The iOS app: Home with the pet, Mini-games tab (download, update, delete), video Feed, wallet. |
| `MiniGameStudio/` | A creator's Unity project with one game, Catch the Prize, and **Mini-game > Publish to live**. |
| `MiniGameSDK/` | The package both projects install: the TypeScript API (`sdk.d.ts`) and built-in components prefabs may use. |
| `LiveServer/` | Cloudflare Worker + R2 that serves the catalog and packages and accepts publishes, deployed at a public web address (currently aliameen.com/petapp). |

- **Puerts (QuickJS backend)** runs mini-game logic. iOS allows interpreted JavaScript; QuickJS needs no JIT; TypeScript gives creators static types and works well with AI coding tools.
- **Unity AssetBundles** carry a game's prefabs, effects and sounds, because art can't be code and doesn't need to be.
- **UI Toolkit** (UXML/USS) for every screen, **DOTween** for UI motion, **URP** for rendering, **VideoPlayer** for the feed.
- **Cloudflare Workers + R2** as the live service: always on, no server to run, files served from the edge.

## Mini-game approach

- **Packaging.** Publishing compiles `game.ts` to `game.js`, builds the game's `Assets/` folder into an AssetBundle per platform (iOS, Mac), and zips them. Each zip is stored under its SHA-256, so its URL never changes meaning.
- **Download and cache.** The catalog lists each game's version and package hash. A game is downloaded the first time it's opened, checked against the hash, stored on device, and unzipped (entries that would escape the folder are refused). Offline, installed games stay playable from the last catalog seen.
- **Update.** The app checks the catalog on launch, on the Games tab, and every 5 s outside a game. A newer version of an installed game triggers "Update available, v19 → v20"; Download fetches and runs it. Delete removes it; Get downloads it again.
- **Options considered.** C# hot-patching (HybridCLR and similar) would give downloaded code the whole engine and every native API, which can't be sandboxed for player-made games and conflicts with guideline 4.7.2. A WebView was ruled out by the brief. Jint, a C# JavaScript interpreter, had IL2CPP problems on iOS. Luau has no maintained Unity binding. Puerts gave a maintained, sandboxable interpreter that runs on iOS today.
- **iOS limits.** No JIT, no downloaded native code or new C#, and no native APIs exposed to downloaded code (guideline 4.7.2). So games are interpreted, the script sees only the `sdk` object (every other global is deleted), and prefabs can only use components already in the app.

## Adding a mini-game, and user-created games

Add `Assets/Games/<id>/` to a creator project with `game.json` (id, title, SDK version), `game.ts`, and an `Assets/` folder whose file names become the keys the code loads. Run **Mini-game > Publish to live**. The server assigns the version and the game appears in every app within seconds, with no app release. For UGC, creators install the same SDK package in their own Unity project; the sandbox already treats game code as untrusted, and packages are verified by hash on upload and download. What's left is per-creator accounts instead of one publish token, moderation before games go live, and the reporting, blocking and age-rating features Apple requires under guideline 4.7.

## How the feed loads and preloads videos

The feed list comes from the same catalog, so it can change without an app release. Three VideoPlayers are pooled: the visible page and its neighbours. Each clip is downloaded to a local cache first and played from disk, because the source MP4s put their index at the end of the file, which iOS can't stream. Downloading the neighbours makes the preload real: the next clip is usually ready before the swipe. Pages show download progress, a "taking too long" state after 15 s with no progress, and a Retry button on failure (one feed entry is a deliberately broken link). Clips fill the screen when their shape is close to the screen's and letterbox otherwise. Playback pauses off the Feed tab and during games, and **Play now** opens the mini-game, downloading it first if needed.

## Running it

Unity 6000.0.68f1 with iOS Build Support. Open `PetAPP`, open `Assets/Scenes/App.unity`, press Play. EditMode tests: Window > General > Test Runner. Publishing from `MiniGameStudio` needs Node.js and the publish token; set `PETAPP_LIVE_URL` and `PETAPP_PUBLISH_TOKEN` to publish to your own deployment of `LiveServer/` (`npx wrangler deploy`).

**Credits.** Pet models by PolyOne Studio (CC BY 4.0, [Sketchfab](https://sketchfab.com/3d-models/free-pack-cartoon-dog-cat-a79da8187fc145a4b6766a402f6505fc)). CC0: Quaternius (monsters, nature), Kenney (particles, sounds), Wenrexa (UI), OpenGameArt ([Snow Theme](https://opengameart.org/content/snow-theme), [Happy Clappy Loop](https://opengameart.org/content/happy-clappy-loop), [Fireplace Sound Loop](https://opengameart.org/content/fireplace-sound-loop)). Lilita One font (SIL OFL). Puerts (BSD-3, Tencent). DOTween (Demigiant license). Feed videos stream from their original URLs.
