using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;

namespace Ashfall.Engine.Occlusion;

public sealed unsafe class NativeUiOccluder
{
    private readonly List<Vector4> _rects = new(64);


    public void BeginFrame(AtkUnitBase* excludeAddon = null)
    {
        _rects.Clear();
        var framework = Framework.Instance();
        if (framework == null) return;
        var uiModule = framework->GetUIModule();
        if (uiModule == null) return;
        var rapture = uiModule->GetRaptureAtkModule();
        if (rapture == null) return;

        ref var list = ref rapture->RaptureAtkUnitManager.AllLoadedUnitsList;
        var count = (int)list.Count;
        var screen = ImGui.GetIO().DisplaySize;

        for (int i = 0; i < count; i++)
        {
            var addon = list.Entries[i].Value;
            if (addon == null) continue;
            if (excludeAddon != null && addon == excludeAddon) continue;
            if (!addon->IsVisible) continue;
            var root = addon->RootNode;
            if (root == null || !root->IsVisible()) continue;

            float w = root->GetWidth() * root->ScaleX;
            float h = root->GetHeight() * root->ScaleY;
            if (w < 4f || h < 4f) continue;

            float l = root->ScreenX;
            float t = root->ScreenY;
            float r = l + w;
            float b = t + h;

            if (w >= screen.X * 0.95f && h >= screen.Y * 0.95f) continue;

            _rects.Add(new Vector4(l, t, r, b));
        }
    }

    public bool IsRectOccluded(Vector4 rect)
    {
        foreach (var r in _rects)
        {
            if (r.Z <= rect.X || rect.Z <= r.X) continue;
            if (r.W <= rect.Y || rect.W <= r.Y) continue;
            return true;
        }
        return false;
    }

    public bool IsPointOccluded(Vector2 point)
    {
        foreach (var r in _rects)
        {
            if (point.X < r.X || point.X >= r.Z) continue;
            if (point.Y < r.Y || point.Y >= r.W) continue;
            return true;
        }
        return false;
    }
}
