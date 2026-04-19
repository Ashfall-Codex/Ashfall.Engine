using System.Numerics;
using Ashfall.Engine.Projection;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;

namespace Ashfall.Engine.Occlusion;

public sealed class RaycastOcclusionStrategy : IOcclusionStrategy
{
    public string Name => "Raycast";
    public bool IsOperational => true;

    public void BeginFrame() { /* stateless */ }
    public void EndFrame() { /* stateless */ }

    public bool IsPixelOccluded(Vector3 targetWorldPos, Vector2 screenPixel)
    {
        if (!WorldProjection.TryScreenPointToRay(screenPixel, out var origin, out var dir)) return false;
        var toTarget = targetWorldPos - origin;
        var projDistance = Vector3.Dot(toTarget, dir);
        if (projDistance < 0.1f) return false;

        if (BGCollisionModule.RaycastMaterialFilter(origin, dir, out var hit, projDistance))
        {
            return hit.Distance < projDistance - 0.3f;
        }
        return false;
    }

    public void Dispose() { /* nothing to dispose */ }
}
