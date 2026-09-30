#ifndef CLAY_CLOUDS_INCLUDED
#define CLAY_CLOUDS_INCLUDED
// Shared cloud field for the planet surface (sky raymarch + ground cloud shadows use the SAME function, so shadows
// sit under the clouds you see). Globals are driven by SurfaceWeather.cs.
//   _CloudShape  : x = coverage 0..1, y = density, z = base altitude (m), w = thickness (m)
//   _CloudOffset : xy = wind drift (m), z = feature scale (m), w = detail strength
// Positions are WORLD metres including the floating-origin offset (_SurfOrigin), so clouds don't jump on rebase.

float4 _CloudShape, _CloudOffset;

float cl_h(float2 p) { p = frac(p * float2(0.1031, 0.1030)); p += dot(p, p.yx + 33.33); return frac((p.x + p.y) * p.x); }
float cl_n(float2 x)
{
    float2 i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(cl_h(i), cl_h(i + float2(1, 0)), f.x), lerp(cl_h(i + float2(0, 1)), cl_h(i + float2(1, 1)), f.x), f.y);
}
float cl_fbm(float2 x)
{
    float s = 0, a = 0.5;
    [unroll] for (int i = 0; i < 5; i++) { s += a * cl_n(x); x = x * 2.03 + 17.1; a *= 0.5; }
    return s;
}
float cl_n3(float3 x)
{
    float3 i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
    float2 uv = i.xy + i.z * float2(37.0, 17.0) + f.xy;
    float a = cl_n(uv), b = cl_n(uv + float2(37.0, 17.0));
    return lerp(a, b, f.z);
}

// Horizontal coverage (0..1) at a world XZ position: where the cloud deck exists.
float CloudCoverage2D(float2 xz)
{
    float2 u = (xz + _CloudOffset.xy) / max(_CloudOffset.z, 1.0);
    float n = cl_fbm(u);
    float c = _CloudShape.x;
    return saturate((n - (1.0 - c) * 0.9) / max(0.18 + c * 0.25, 0.05));
}

// 3D density inside the slab: coverage × vertical profile (flat bases, rounded tops), eroded by detail noise.
float CloudDensity(float3 pos)
{
    float h = (pos.y - _CloudShape.z) / max(_CloudShape.w, 1.0);
    if (h <= 0.0 || h >= 1.0) return 0.0;
    float cov = CloudCoverage2D(pos.xz);
    if (cov <= 0.001) return 0.0;
    float prof = smoothstep(0.0, 0.12, h) * smoothstep(1.0, 0.25 + 0.5 * (1.0 - cov), h);
    float det = cl_n3((pos + float3(_CloudOffset.x, 0, _CloudOffset.y) * 1.3) / 420.0);
    return saturate(cov * prof - det * _CloudOffset.w * (1.0 - cov * 0.5)) * _CloudShape.y;
}

// Transmittance of sunlight through the deck above a ground point (cheap: one coverage lookup along the sun ray).
float CloudShadow(float3 wpos, float3 sunDir)
{
    if (_CloudShape.x <= 0.01) return 1.0;
    float mid = _CloudShape.z + _CloudShape.w * 0.4;
    float t = (mid - wpos.y) / max(sunDir.y, 0.08);
    float cov = CloudCoverage2D(wpos.xz + sunDir.xz * t);
    return exp(-cov * _CloudShape.y * (1.5 + _CloudShape.w / 1500.0));
}
#endif
