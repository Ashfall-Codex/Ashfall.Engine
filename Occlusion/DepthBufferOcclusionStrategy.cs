using System.Numerics;
using Ashfall.Engine.Platform;
using Ashfall.Engine.Projection;
using SharpDX.Direct3D11;
using FcsKernel = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using SampleDescription = SharpDX.DXGI.SampleDescription;

namespace Ashfall.Engine.Occlusion;

public sealed class DepthBufferOcclusionStrategy : IOcclusionStrategy
{
    private readonly Action<string, Exception?>? _logWarn;
    private readonly Action<string>? _logInfo;

    private Device? _device;
    private DeviceContext? _context;
    private Texture2D? _stagingA;
    private Texture2D? _stagingB;
    private bool _readyA;
    private bool _readyB;
    private long _frameIndex;
    private int _width;
    private int _height;
    private bool _mapped;
    private Texture2D? _mappedStaging;
    private SharpDX.DataBox _mapBox;
    private int _consecutiveFailures;
    private bool _permanentlyDisabled;
    private bool _disposed;
    private const int MaxFailuresBeforeDisable = 3;
    // DoNotWait évite le coût fixe d'un Map synchrone, mais sur DXVK il retourne un DataBox
    // vide silencieusement (occlusion désactivée). On l'active donc uniquement sur D3D11 natif.
    private readonly bool _useDoNotWait = PlatformDetector.IsNativeDirectX;

    public string Name => "DepthBuffer";
    public bool IsOperational => !_disposed && !_permanentlyDisabled && _device != null && _context != null;

    public DepthBufferOcclusionStrategy(Action<string>? logInfo = null, Action<string, Exception?>? logWarn = null)
    {
        _logInfo = logInfo;
        _logWarn = logWarn;
        try
        {
            unsafe
            {
                var devPtr = FcsKernel.Device.Instance();
                if (devPtr == null || devPtr->D3D11Forwarder == null || devPtr->D3D11DeviceContext == null)
                {
                    _logWarn?.Invoke("DepthBuffer strategy: game D3D11 device/context unavailable at init", null);
                    return;
                }
                _device = new Device((nint)devPtr->D3D11Forwarder);
                _context = new DeviceContext((nint)devPtr->D3D11DeviceContext);
            }
            _logInfo?.Invoke("DepthBuffer strategy initialized");
        }
        catch (Exception ex)
        {
            _logWarn?.Invoke("DepthBuffer strategy init failed", ex);
        }
    }

    public unsafe void BeginFrame()
    {
        if (!IsOperational) return;
        if (_mapped && _mappedStaging != null)
        {
            try { _context?.UnmapSubresource(_mappedStaging, 0); } catch { /* ignored */ }
        }
        _mapped = false;
        _mappedStaging = null;

        // Source principale : RenderTargetManager.Unk70 (offset 0x70, marqué "Depth/Stencil?" dans FCS)
        // contient la vraie depth utilisée pour le rendu 3D dans tous les presets graphiques.
        // Fallback : SwapChain.DepthStencil (dans certains cas plus simples ça colle aussi).
        void* gameDepthPtr = null;
        var rtMgr = FFXIVClientStructs.FFXIV.Client.Graphics.Render.RenderTargetManager.Instance();
        if (rtMgr != null)
        {
            var unk70 = *((FcsKernel.Texture**)((byte*)rtMgr + 0x70));
            if (unk70 != null) gameDepthPtr = unk70->D3D11Texture2D;
        }
        if (gameDepthPtr == null)
        {
            var devPtr = FcsKernel.Device.Instance();
            if (devPtr != null && devPtr->SwapChain != null && devPtr->SwapChain->DepthStencil != null)
                gameDepthPtr = devPtr->SwapChain->DepthStencil->D3D11Texture2D;
        }
        if (gameDepthPtr == null) return;

        Texture2D? gameDepth = null;
        try
        {
            gameDepth = new Texture2D((nint)gameDepthPtr);
            var desc = gameDepth.Description;
            if (desc.Width <= 0 || desc.Height <= 0) return;

            if (_stagingA == null || _stagingB == null || _width != desc.Width || _height != desc.Height)
            {
                InvalidateStaging();
                _width = desc.Width;
                _height = desc.Height;
                _stagingA = CreateStaging(desc);
                _stagingB = CreateStaging(desc);
                _readyA = false;
                _readyB = false;
                _frameIndex = 0;
                _logInfo?.Invoke($"DepthBuffer source: {desc.Width}x{desc.Height} fmt={desc.Format} bindFlags={desc.BindFlags} sampleCount={desc.SampleDescription.Count} sampleQuality={desc.SampleDescription.Quality} mips={desc.MipLevels} (double-buffered)");
                return; // skip cette frame, aucun staging n'a encore de contenu
            }

            bool useA = (_frameIndex & 1L) == 0L;
            var writeStaging = useA ? _stagingA : _stagingB;
            var readStaging = useA ? _stagingB : _stagingA;
            bool readReady = useA ? _readyB : _readyA;

            _context!.CopyResource(gameDepth, writeStaging);
            if (useA) _readyA = true; else _readyB = true;
            if (readReady)
            {
                try
                {
                    var flags = _useDoNotWait ? MapFlags.DoNotWait : MapFlags.None;
                    _mapBox = _context.MapSubresource(readStaging, 0, MapMode.Read, flags);
                    if (_mapBox.DataPointer == IntPtr.Zero)
                    {
                        _mapped = false;
                    }
                    else
                    {
                        _mappedStaging = readStaging;
                        _mapped = true;
                        _consecutiveFailures = 0;
                    }
                }
                catch (SharpDX.SharpDXException ex) when (ex.ResultCode == SharpDX.DXGI.ResultCode.WasStillDrawing)
                {
                    // Possible uniquement avec DoNotWait : on saute l'occlusion cette frame.
                    _mapped = false;
                }
            }

            _frameIndex++;
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _logWarn?.Invoke($"DepthBuffer capture failed ({_consecutiveFailures}/{MaxFailuresBeforeDisable})", ex);
            InvalidateStaging();
            if (_consecutiveFailures >= MaxFailuresBeforeDisable)
            {
                _permanentlyDisabled = true;
                _logWarn?.Invoke($"DepthBuffer strategy permanently disabled after {_consecutiveFailures} failures", null);
            }
        }
        finally
        {
            try { gameDepth?.Dispose(); } catch { /* ignored */ }
        }
    }

    private Texture2D CreateStaging(Texture2DDescription source) => new(_device!, new Texture2DDescription
    {
        Width = source.Width,
        Height = source.Height,
        MipLevels = 1,
        ArraySize = 1,
        Format = source.Format,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Staging,
        BindFlags = BindFlags.None,
        CpuAccessFlags = CpuAccessFlags.Read,
        OptionFlags = ResourceOptionFlags.None,
    });

    public void EndFrame()
    {
        if (_mapped && _context != null && _mappedStaging != null)
        {
            try { _context.UnmapSubresource(_mappedStaging, 0); } catch { /* ignored */ }
            _mapped = false;
            _mappedStaging = null;
        }
    }

    public unsafe bool IsPixelOccluded(Vector3 targetWorldPos, Vector2 screenPixel)
    {
        if (!_mapped || _mappedStaging == null) return false;
        if (!WorldProjection.TryProjectToScreen(targetWorldPos, out _, out var expectedZ)) return false;

        int sx = (int)screenPixel.X;
        int sy = (int)screenPixel.Y;
        if (sx < 0 || sy < 0 || sx >= _width || sy >= _height) return false;

        var basePtr = (byte*)_mapBox.DataPointer.ToPointer();
        if (basePtr == null) return false;
        int rowPitch = _mapBox.RowPitch;
        int offset = sy * rowPitch + sx * 4;
        uint raw = *(uint*)(basePtr + offset);
        uint depth24 = raw & 0x00FFFFFFu;
        float sampled = depth24 / (float)0xFFFFFF;

        // Reverse-Z : plus grand = plus proche. Occluded si le pixel samplé est devant la cible.
        return sampled > expectedZ + 0.0005f;
    }

    private void InvalidateStaging()
    {
        if (_mapped && _context != null && _mappedStaging != null)
        {
            try { _context.UnmapSubresource(_mappedStaging, 0); } catch { /* ignored */ }
        }
        _mapped = false;
        _mappedStaging = null;
        try { _stagingA?.Dispose(); } catch { /* ignored */ }
        try { _stagingB?.Dispose(); } catch { /* ignored */ }
        _stagingA = null;
        _stagingB = null;
        _readyA = false;
        _readyB = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        InvalidateStaging();
        _device = null;
        _context = null;
    }
}
