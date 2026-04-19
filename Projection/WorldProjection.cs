using System.Numerics;
using System.Runtime.CompilerServices;
using FcsKernel = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FcsMath = FFXIVClientStructs.FFXIV.Common.Math;

namespace Ashfall.Engine.Projection;

public static class WorldProjection
{

    public static unsafe bool TryProjectToScreen(Vector3 worldPos, out Vector2 screenPos, out float depth)
    {
        screenPos = default;
        depth = 0f;

        var control = FFXIVClientStructs.FFXIV.Client.Game.Control.Control.Instance();
        if (control == null) return false;

        var vpFcs = control->ViewProjectionMatrix;
        var vp = Unsafe.As<FcsMath.Matrix4x4, Matrix4x4>(ref vpFcs);
        var clip = Vector4.Transform(new Vector4(worldPos, 1f), vp);
        if (clip.W <= 0.01f) return false;
        var invW = 1f / clip.W;

        var devPtr = FcsKernel.Device.Instance();
        if (devPtr == null) return false;
        float screenW = devPtr->Width;
        float screenH = devPtr->Height;
        if (screenW <= 0 || screenH <= 0) return false;

        screenPos = new Vector2((clip.X * invW + 1f) * screenW * 0.5f, (1f - clip.Y * invW) * screenH * 0.5f);
        depth = clip.Z * invW;
        return true;
    }


    public static unsafe bool TryScreenPointToRay(Vector2 screenPoint, out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = default;

        var control = FFXIVClientStructs.FFXIV.Client.Game.Control.Control.Instance();
        if (control == null) return false;
        var vpFcs = control->ViewProjectionMatrix;
        var vp = Unsafe.As<FcsMath.Matrix4x4, Matrix4x4>(ref vpFcs);
        if (!Matrix4x4.Invert(vp, out var invVp)) return false;

        var devPtr = FcsKernel.Device.Instance();
        if (devPtr == null) return false;
        float screenW = devPtr->Width;
        float screenH = devPtr->Height;
        if (screenW <= 0 || screenH <= 0) return false;

        float ndcX = (screenPoint.X / screenW) * 2f - 1f;
        float ndcY = 1f - (screenPoint.Y / screenH) * 2f;

        var nearClip = new Vector4(ndcX, ndcY, 1f, 1f);
        var farClip = new Vector4(ndcX, ndcY, 0f, 1f);

        var nearW = Vector4.Transform(nearClip, invVp);
        var farW = Vector4.Transform(farClip, invVp);
        if (MathF.Abs(nearW.W) < 1e-5f || MathF.Abs(farW.W) < 1e-5f) return false;

        var nearP = new Vector3(nearW.X, nearW.Y, nearW.Z) / nearW.W;
        var farP = new Vector3(farW.X, farW.Y, farW.Z) / farW.W;
        var delta = farP - nearP;
        var len = delta.Length();
        if (len < 1e-4f) return false;
        origin = nearP;
        direction = delta / len;
        return true;
    }
}
