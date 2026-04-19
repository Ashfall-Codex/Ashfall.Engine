using Ashfall.Engine.Occlusion;
using Ashfall.Engine.Platform;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;

namespace Ashfall.Engine;
public sealed class OverlayEngine : IDisposable
{
    private readonly IOcclusionStrategy _worldOcclusion;
    private readonly NativeUiOccluder _nativeUi;
    private bool _disposed;

    public NativeUiOccluder NativeUi => _nativeUi;
    public IOcclusionStrategy WorldOcclusion => _worldOcclusion;
    public GraphicsBackend Backend => PlatformDetector.Backend;

    public OverlayEngine(IOcclusionStrategy worldOcclusion, NativeUiOccluder nativeUi)
    {
        _worldOcclusion = worldOcclusion;
        _nativeUi = nativeUi;
    }
    
    public static OverlayEngine CreateDefault(
        bool preferHighPrecision = true,
        bool? forceDepthBuffer = null,
        Action<string>? logInfo = null,
        Action<string, Exception?>? logWarn = null)
    {
        PlatformDetector.Prime();
        var raycast = new RaycastOcclusionStrategy();
        IOcclusionStrategy strategy;

        // DXMT (XIV on Mac avec traduction directe D3D11→Metal) crash sur CopyResource de depth
        // via son chemin MTL_DXGI_FORMAT_EMULATED_D24 — détection fiable via PE metadata de d3d11.dll.
        // On force le raycast dans ce cas, peu importe la préférence utilisateur.
        bool blockedByDxmt = PlatformDetector.IsDXMT;
        bool useDepth = !blockedByDxmt && (forceDepthBuffer ?? preferHighPrecision);

        if (useDepth)
        {
            var depth = new DepthBufferOcclusionStrategy(logInfo, logWarn);
            strategy = new CompositeOcclusionStrategy(depth, raycast);
        }
        else
        {
            strategy = raycast;
        }

        logInfo?.Invoke($"Ashfall.Engine: world occlusion = {strategy.Name} ({PlatformDetector.DebugInfo}, backend={PlatformDetector.Backend})");

        return new OverlayEngine(strategy, new NativeUiOccluder());
    }
    
    public unsafe void BeginFrame(AtkUnitBase* excludedAddonForUi = null)
    {
        _nativeUi.BeginFrame(excludedAddonForUi);
        _worldOcclusion.BeginFrame();
    }

    public void EndFrame() => _worldOcclusion.EndFrame();
    
    public bool IsOccluded(Vector3 targetWorldPos, Vector2 centerPixel, Vector4 overlayRect)
    {
        if (_nativeUi.IsRectOccluded(overlayRect)) return true;
        return _worldOcclusion.IsPixelOccluded(targetWorldPos, centerPixel);
    }

    public bool IsWorldOccluded(Vector3 targetWorldPos, Vector2 centerPixel)
        => _worldOcclusion.IsPixelOccluded(targetWorldPos, centerPixel);

    public bool IsNativeUiOccluded(Vector4 rect) => _nativeUi.IsRectOccluded(rect);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _worldOcclusion.Dispose();
    }
}
