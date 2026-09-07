# Prototype 1

A 2D platformer prototype in **Unity 6** (Universal Render Pipeline, Input System). The heart of it is a
self-contained character controller with double jump, Blasphemous-style wall cling and wall jumps,
coyote time, jump buffering, one-way platforms and moving platforms.

## Getting started

1. Install **Unity 6000.5.10f1** through Unity Hub (use this exact version so everyone's `Library`
   cache and serialization match).
2. Clone this repository and add the folder in Unity Hub (**Projects → Add → Add project from disk**).
   The first open imports packages and takes a few minutes.
3. Open `Assets/Platformer/Scenes/PlatformerDemo.unity` and press **Play**.

| Action | Keyboard | Gamepad |
|---|---|---|
| Run | A / D or ← / → | left stick, d-pad |
| Jump (hold for higher) | Space | South button |
| Double jump | Space again in the air | South again |
| Grab a wall | push toward it while airborne | same |
| Jump off a wall / let go | Space / S | South / down |
| Drop through a plank | S + Space | down + South |

## Layout

```
Assets/
  Platformer/            everything for the platformer (see Assets/Platformer/README.md)
    Scripts/PlayerController.cs   the character controller (every field has a tooltip)
    Scripts/InputActionsSource.cs feeds it from InputSystem_Actions (Player/Move, Player/Jump)
    Scripts/CameraFollow.cs, MovingPlatform.cs
    Editor/PlatformerSceneSetup.cs  rebuilds the demo scene: menu Platformer > Create Demo Scene
    Scenes/PlatformerDemo.unity, Materials/
  InputSystem_Actions.inputactions  project-wide input actions
  Scenes/SampleScene.unity          the URP template scene (unused)
  Settings/                          URP render pipeline assets
```

## Working together

- **Never commit** `Library/`, `Temp/`, `Logs/`, `UserSettings/` (already in `.gitignore`).
  Assets are serialized as text (*Project Settings → Editor → Asset Serialization: Force Text*): keep it that way.
- **Scenes and prefabs merge badly.** Agree who is editing a scene before touching it, and build features
  as prefabs or in their own scene so people rarely collide. If a merge conflict does hit a `.unity` /
  `.prefab` file, set up Unity's Smart Merge (instructions in `.gitattributes`) instead of resolving by hand.
- **Branch per feature**, open a pull request against `main`, get one review, merge. Keep PRs small.
- **Binary assets** (art, audio): once they arrive, install Git LFS and track them (see `.gitattributes`).
- **Same Unity version for everyone.** Upgrading Unity is its own PR.
- Commit `.meta` files together with the assets they belong to; a missing `.meta` breaks references for everyone else.
