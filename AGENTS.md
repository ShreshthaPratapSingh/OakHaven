# AGENTS.md

Unity 6.6 project (`6000.6.0f1`), URP. First-person→third-person networked survival/crafting game. No README content, no CI, no tests.

## Build / verify
- There is no CLI compile/lint/test. C# compiles only inside the Unity Editor; runtime verification is entering Play mode on `Assets/Scenes/SampleScene.unity` (the only scene in build settings).
- Do not run `dotnet`/`msbuild` against the repo. `*.csproj` and `OakHaven.slnx` are Unity-generated and gitignored; `dotnet.defaultSolution` only matters for IDE IntelliSense.
- No assembly definitions: everything under `Assets/Scripts` is in `Assembly-CSharp`; `Assets/Scripts/Editor` + `Assets/Editor` are in `Assembly-CSharp-Editor`.

## Editor-generated files — do not hand-edit
- `Assets/Scripts/Input/InputSystem_Actions.cs` is generated from `Assets/InputSystem_Actions.inputactions`. Change the `.inputactions` asset, not the `.cs`.
- Every asset has a paired `.meta` (GUID). Never delete, rename, or create an asset without its `.meta`; missing/duplicated GUIDs break scene references.
- Editing Unity YAML assets (`.unity`, `.prefab`, `.asset`) by hand is error-prone. Prefer editor menu tools or `SerializedObject` scripts (see `Assets/Scripts/Editor/NetworkingSetup.cs` for the pattern).

## Architecture
- Networking = Unity Netcode for GameObjects (`com.unity.netcode.gameobjects` 2.13.2) + Unity Gaming Services Relay/Auth. Server-authoritative:
  - Clients only read input and do local detection when `IsOwner` (e.g. `PlayerGathering`, `PlayerCrafting`, `PlayerController`).
  - State changes go through `ServerRpc` and the server validates before mutating; state replicates via `NetworkVariable` (ServerWrite / EveryoneRead).
  - Shared world objects (`ResourceNode`, `Workstation`) use `ServerRpc(RequireOwnership = false)`. This is exploitable by design — always validate on the server, never trust client-sent amounts/ids.
- Player prefab (`Assets/Prefabs/NetworkPlayer.prefab`) holds two separate inventories, both extending `BaseInventory` (weight- + stack-aware): `Inventory` = equipment/tools, `ResourceInventory` = raw materials. They have independent slot/weight limits. UI currently reads the equipment one.
- Data-driven via ScriptableObjects in `Assets/Data`: `ItemDefinition`, `CraftingRecipe`, `WorkstationDefinition`. Items/recipes are identified over the network by `string` id (`itemId`/`recipeId`) resolved through the static `ItemDatabase` — assets themselves are never sent.
- `ItemDatabaseLoader` must exist in any scene using inventory/crafting; it clears and repopulates `ItemDatabase` on `Awake`.
- Initialization order is fragile: `PlayerController` disables `CharacterController` on spawn and re-enables it after waiting 2 frames so the connection-approval position can apply. Don't remove that coroutine.
- Spawn position is hard-coded in two places — `RelayManager.spawnPosition` (625.08, 4.5, 436.94) and a `PlayerController` fallback. Keep them in sync if the level origin moves.

## Editor automation
- `Tools > OakHaven > Setup Networking` creates/configures `NetworkManager`, `UnityTransport`, `NetworkPlayer.prefab`, relay UI, and registers the player prefab. `Rebuild Player Prefab` recreates it. Idempotent.
- Networking requires the project linked to UGS and Relay enabled in the Unity Dashboard; without it Relay sign-in fails at runtime.

## Conventions
- Commit messages: `type: description` (`feat:`, `setup:`, `polish:`, `fix:`). Work lands via PRs into feature branches (e.g. `feature/levelDesign`).
- Git LFS is configured (`.gitattributes`) for images/audio/models/fonts. Unity YAML files use `merge=unityyamlmerge`. Commit the `.meta` alongside every asset change.
