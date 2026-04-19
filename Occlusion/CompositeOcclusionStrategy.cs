using System.Numerics;

namespace Ashfall.Engine.Occlusion;

public sealed class CompositeOcclusionStrategy : IOcclusionStrategy
{
    private readonly IOcclusionStrategy _primary;
    private readonly IOcclusionStrategy _fallback;

    public CompositeOcclusionStrategy(IOcclusionStrategy primary, IOcclusionStrategy fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public string Name => $"Composite({_primary.Name} + {_fallback.Name})";
    public bool IsOperational => _primary.IsOperational || _fallback.IsOperational;

    public void BeginFrame()
    {
        if (_primary.IsOperational) _primary.BeginFrame();
        _fallback.BeginFrame();
    }

    public void EndFrame()
    {
        _primary.EndFrame();
        _fallback.EndFrame();
    }

    public bool IsPixelOccluded(Vector3 targetWorldPos, Vector2 screenPixel)
    {
        if (_primary.IsOperational)
            return _primary.IsPixelOccluded(targetWorldPos, screenPixel);
        return _fallback.IsPixelOccluded(targetWorldPos, screenPixel);
    }

    public void Dispose()
    {
        _primary.Dispose();
        _fallback.Dispose();
    }
}
