#ifndef SURF_AMBIENT_INCLUDED
#define SURF_AMBIENT_INCLUDED
// Planet-surface ambient: the SAME L2 spherical harmonics as RenderSettings.ambientProbe, pushed as globals every frame
// by SurfaceWorld. Instanced draws (rocks, flora, grass) don't reliably get the live per-draw SH — they kept daylight
// ambient after nightfall — so every surface shader reads these instead of SampleSH.
float4 _SSHAr, _SSHAg, _SSHAb, _SSHBr, _SSHBg, _SSHBb, _SSHC;
float3 SurfSH(float3 n)
{
    float4 n4 = float4(n, 1.0);
    float3 x1 = float3(dot(_SSHAr, n4), dot(_SSHAg, n4), dot(_SSHAb, n4));
    float4 vB = n.xyzz * n.yzzx;
    float3 x2 = float3(dot(_SSHBr, vB), dot(_SSHBg, vB), dot(_SSHBb, vB));
    float3 x3 = _SSHC.rgb * (n.x * n.x - n.y * n.y);
    return max(0.0, x1 + x2 + x3);
}
#endif
