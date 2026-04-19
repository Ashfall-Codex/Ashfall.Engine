using System.Diagnostics;
using D3D11Device = SharpDX.Direct3D11.Device;
using DxgiDevice = SharpDX.DXGI.Device;

namespace Ashfall.Engine.Platform;


// Détecte la pile graphique sous laquelle le jeu tourne (DX11 natif Windows,DXVK sur Linux/Proton/Mac-via-Vulkan, DXMT sur macOS via XIV on Mac avec traduction directe vers Metal).

public static class PlatformDetector
{
    private static bool _initialized;
    private static GraphicsBackend _backend = GraphicsBackend.Unknown;
    private static string _adapterDescription = string.Empty;
    private static uint _vendorId;
    private static uint _deviceId;
    private static long _driverVersion;

    public static GraphicsBackend Backend { get { EnsureInitialized(); return _backend; } }
    public static string AdapterDescription { get { EnsureInitialized(); return _adapterDescription; } }
    public static uint VendorId { get { EnsureInitialized(); return _vendorId; } }
    public static uint DeviceId { get { EnsureInitialized(); return _deviceId; } }

    private static string _moduleSignature = string.Empty;

    public static string DebugInfo
    {
        get
        {
            EnsureInitialized();
            return $"desc=\"{_adapterDescription}\" vendor=0x{_vendorId:X4} device=0x{_deviceId:X4} driver={_driverVersion:X} d3d11=\"{_moduleSignature}\"";
        }
    }

    public static bool IsDXMT => Backend == GraphicsBackend.DXMT;
    public static bool IsDXVK => Backend == GraphicsBackend.DXVK;
    public static bool IsNativeDirectX => Backend == GraphicsBackend.NativeDX11;

    public static void Prime() => EnsureInitialized();

    public static void Override(GraphicsBackend backend)
    {
        _backend = backend;
        _initialized = true;
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            unsafe
            {
                var devPtr = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
                if (devPtr == null) return;
                using var dev = new D3D11Device((nint)devPtr->D3D11Forwarder);
                using var dxgiDev = dev.QueryInterfaceOrNull<DxgiDevice>();
                if (dxgiDev == null) return;
                using var adapter = dxgiDev.Adapter;
                var desc = adapter.Description;
                _adapterDescription = desc.Description?.Trim('\0').Trim() ?? string.Empty;
                _vendorId = (uint)desc.VendorId;
                _deviceId = (uint)desc.DeviceId;
                _driverVersion = 0;
                _backend = Classify(_adapterDescription, _vendorId);
            }
        }
        catch
        {
            _backend = GraphicsBackend.Unknown;
        }
    }

    /// <summary>
    /// Inspecte le module <c>d3d11.dll</c> chargé dans le process pour déterminer sa provenance.
    /// DXMT et DXVK ont des métadonnées PE (ProductName / CompanyName / FileDescription) distinctes.
    /// </summary>
    private static GraphicsBackend? DetectFromD3d11Module()
    {
        try
        {
            foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
            {
                if (!string.Equals(module.ModuleName, "d3d11.dll", StringComparison.OrdinalIgnoreCase)) continue;
                var info = FileVersionInfo.GetVersionInfo(module.FileName);
                _moduleSignature = $"{info.ProductName};{info.CompanyName};{info.FileDescription}".Trim();
                string metadata = string.Join("|",
                    info.ProductName ?? string.Empty,
                    info.CompanyName ?? string.Empty,
                    info.FileDescription ?? string.Empty,
                    info.InternalName ?? string.Empty,
                    info.OriginalFilename ?? string.Empty).ToLowerInvariant();

                if (metadata.Contains("dxmt") || metadata.Contains("d3d11-to-metal"))
                    return GraphicsBackend.DXMT;
                if (metadata.Contains("dxvk"))
                    return GraphicsBackend.DXVK;
                break;
            }
        }
        catch { /* ignored */ }
        return null;
    }

    private static GraphicsBackend Classify(string desc, uint vendorId)
    {
        // 1) Essai via PE metadata de d3d11.dll (plus fiable que DXGI quand le GPU est Apple).
        var fromModule = DetectFromD3d11Module();
        if (fromModule.HasValue) return fromModule.Value;

        if (string.IsNullOrEmpty(desc)) return GraphicsBackend.Unknown;
        var d = desc.ToLowerInvariant();

        // 2) Marqueurs DXVK courants dans la description DXGI.
        if (d.Contains("dxvk") || d.Contains("(vk)") || d.Contains("vulkan") || d.Contains("moltenvk"))
            return GraphicsBackend.DXVK;

        // 3) Vendor Apple sans marqueur DXVK → très probablement DXMT (conservateur).
        if (vendorId == 0x106B || d.Contains("apple m") || d.StartsWith("apple ", StringComparison.Ordinal))
            return GraphicsBackend.DXMT;

        // 4) Tout le reste : NVIDIA / AMD / Intel / Arc → DX11 natif Windows.
        return GraphicsBackend.NativeDX11;
    }
}

public enum GraphicsBackend
{
    Unknown,
    NativeDX11,
    DXVK,
    DXMT,
}
