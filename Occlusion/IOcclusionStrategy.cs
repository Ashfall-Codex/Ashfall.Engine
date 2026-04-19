using System.Numerics;

namespace Ashfall.Engine.Occlusion;


public interface IOcclusionStrategy : IDisposable
{
    string Name { get; }

    void BeginFrame();

    void EndFrame();

    bool IsPixelOccluded(Vector3 targetWorldPos, Vector2 screenPixel);

    bool IsOperational { get; }
}
