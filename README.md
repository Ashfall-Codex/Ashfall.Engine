# Ashfall.Engine

*[Read in English](./README.en.md)*

Boîte à outils C# / Dalamud pour dessiner des overlays ImGui au-dessus du rendu 3D de FINAL FANTASY XIV tout en respectant **l'occlusion par la géométrie du monde et l'UI native du jeu**.

Conçu pour les plugins Ashfall-Codex (en particulier [UmbraSync](https://github.com/Ashfall-Codex/UmbraClient)), mais utilisable par tout plugin Dalamud qui a besoin d'icônes flottantes, de bulles ou de labels correctement cachés derrière les murs, arbres, mobilier housing et menus.

## Ce que résout la lib

Un plugin Dalamud qui dessine via `ImGui.GetBackgroundDrawList()` ou équivalent dessine **par-dessus** tout — pas de depth test contre la scène 3D, pas de respect des menus ouverts. Visuellement, les icônes traversent les décors.

`Ashfall.Engine` fournit trois briques combinables :

- **`WorldOcclusion`** — projection monde ↔ écran et rayons écran → monde, à partir de la `ViewProjectionMatrix` pré-calculée par le jeu (reverse-Z géré).
- **`IOcclusionStrategy`** — abstraction des tests d'occlusion 3D :
  - `DepthBufferOcclusionStrategy` — lit le depth buffer GPU du jeu via SharpDX (précis, Windows + Linux/Proton/DXVK + macOS sans DXMT).
  - `RaycastOcclusionStrategy` — lance un rayon de collision depuis la caméra vers le pixel testé (`BGCollisionModule`), universel mais ignore les objets sans collision.
  - `CompositeOcclusionStrategy` — choisit la première stratégie opérationnelle avec fallback automatique.
- **`NativeUiOccluder`** — scan les addons ATK visibles et teste si un rectangle d'overlay est recouvert par un menu / popup du jeu.

Le tout est orchestré par **`OverlayEngine`**, une façade qui sélectionne la bonne stratégie selon la plateforme détectée :

| Plateforme | Stratégie par défaut |
|---|---|
| Windows natif | `DepthBuffer` (fallback `Raycast`) |
| Linux / Proton / DXVK | `DepthBuffer` (fallback `Raycast`) |
| macOS via XIV on Mac (DXVK) | `DepthBuffer` (fallback `Raycast`) |
| macOS via XIV on Mac (DXMT) | `Raycast` uniquement (DXMT crash sur `CopyResource` de depth) |

La détection DXMT est faite en inspectant les métadonnées PE de `d3d11.dll` chargé dans le process.

## Utilisation

```csharp
// Création (une fois au démarrage du plugin) :
var engine = OverlayEngine.CreateDefault(
    preferHighPrecision: true,
    logInfo: msg => logger.LogInformation("{Msg}", msg),
    logWarn: (msg, ex) => logger.LogWarning(ex, "{Msg}", msg));

// À chaque frame, dans ton UiBuilder.Draw :
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

## Exigences

- `.NET 10` + `Dalamud.NET.Sdk 14.0.1+`
- `FFXIVClientStructs` (via Dalamud)
- `SharpDX.Direct3D11` (pour la stratégie DepthBuffer)

## Licence

AGPL-3.0 — voir [LICENSE](./LICENSE).

## Crédit

Construit pour [Ashfall-Codex](https://github.com/Ashfall-Codex). Inspiré des patterns d'occlusion de [LightlessSync](https://git.lightless-sync.org/Lightless-Sync/LightlessClient) et [StudioFourteen](https://github.com/UnlostWorld/StudioFourteen) pour l'accès au depth buffer côté DX11.
