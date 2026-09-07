# Platformer

- `Scripts/PlayerController.cs` — the character controller (self-contained; see the tooltips on every field).
- `Scripts/InputActionsSource.cs` — feeds it from the project-wide `InputSystem_Actions` (Player/Move, Player/Jump). Remove it to use raw keyboard/gamepad polling instead.
- `Scripts/CameraFollow.cs`, `Scripts/MovingPlatform.cs` — camera and a kinematic platform.
- `Editor/PlatformerSceneSetup.cs` — builds `Scenes/PlatformerDemo.unity` from primitives. Menu: **Platformer > Create Demo Scene**.

Controls: A/D or arrows run, Space jumps (hold for higher), Space again in the air double-jumps, push into a wall in the air to grab it and scrape slowly down (Space jumps off, S drops), S + Space drops through planks. Gamepad: left stick / d-pad + South button.
