# Ashfall.Engine

*[Lire en français](./README.md)*

C# / Dalamud toolkit for drawing ImGui overlays on top of FINAL FANTASY XIV's 3D render while **respecting occlusion from world geometry and the game's native UI**.

Designed for Ashfall-Codex plugins (in particular [UmbraSync](https://github.com/Ashfall-Codex/UmbraClient)), but usable by any Dalamud plugin that needs floating icons, bubbles or labels properly hidden behind walls, trees, housing decor and open menus.

## What it solves

A Dalamud plugin that draws via `ImGui.GetBackgroundDrawList()` or equivalent draws **on top of** everything — no depth test against the 3D scene, no respect for open menus. Icons visually bleed through decor.

`Ashfall.Engine` ships three composable building blocks:

- **`WorldProjection`** — world ↔ screen projection and screen → world rays using the game's pre-computed `ViewProjectionMatrix` (reverse-Z handled).
- **`IOcclusionStrategy`** — 3D occlusion test abstraction:
  - `DepthBufferOcclusionStrategy` — reads the game's GPU depth buffer via SharpDX (precise, Windows + Linux/Proton/DXVK + macOS without DXMT).
  - `RaycastOcclusionStrategy` — casts a collision ray from the camera to the tested pixel (`BGCollisionModule`), universal but ignores decorations without collision.
  - `CompositeOcclusionStrategy` — picks the first operational strategy with automatic fallback.
- **`NativeUiOccluder`** — scans visible ATK addons and tests whether an overlay rect is covered by a game menu / popup.

Everything is orchestrated by **`OverlayEngine`**, a façade that selects the right strategy based on detected platform:

| Platform | Default strategy |
|---|---|
| Windows native | `DepthBuffer` (fallback `Raycast`) |
| Linux / Proton / DXVK | `DepthBuffer` (fallback `Raycast`) |
| macOS via XIV on Mac (DXVK) | `DepthBuffer` (fallback `Raycast`) |
| macOS via XIV on Mac (DXMT) | `Raycast` only (DXMT crashes on `CopyResource` of depth) |

DXMT detection is performed by inspecting the PE metadata of the `d3d11.dll` loaded in the process.

## Usage

```csharp
// Create (once at plugin start):
var engine = OverlayEngine.CreateDefault(
    preferHighPrecision: true,
    logInfo: msg => logger.LogInformation("{Msg}", msg),
    logWarn: (msg, ex) => logger.LogWarning(ex, "{Msg}", msg));

// Each frame, inside your UiBuilder.Draw:
unsafe
{
    engine.BeginFrame(excludedAddonForUi: nameplateAddon); // capture depth + scan addons
    try
    {
        foreach (var pair in visiblePairs)
        {
            var worldPos = new Vector3(pair.Position.X, pair.Position.Y + 2.2f, pair.Position.Z);
            var iconCenter = new Vector2(screenX, screenY);
            var iconRect = new Vector4(left, top, right, bottom);

            if (engine.IsOccluded(worldPos, iconCenter, iconRect))
                continue;

            drawList.AddImage(texture, drawPos, drawPos + size);
        }
    }
    finally
    {
        engine.EndFrame(); // release transient resources (map/unmap, etc.)
    }
}
```

## Requirements

- `.NET 10` + `Dalamud.NET.Sdk 14.0.1+`
- `FFXIVClientStructs` (via Dalamud)
- `SharpDX.Direct3D11` (for the DepthBuffer strategy)

## License

AGPL-3.0 — see [LICENSE](./LICENSE).

## Credits

Built for [Ashfall-Codex](https://github.com/Ashfall-Codex). Inspired by the occlusion patterns from [LightlessSync](https://git.lightless-sync.org/Lightless-Sync/LightlessClient) and [StudioFourteen](https://github.com/UnlostWorld/StudioFourteen) for DX11-side depth buffer access.
