// The software renderer's column march, including its cut slab and byte truncation.
// Each thread owns one screen column; no atomics or inter-column ordering are needed.
// A structured buffer avoids driver specialization/recompilation when camera constants change.
// It is uniform across the dispatch, but remains data rather than shader specialization input.
StructuredBuffer<float4> cameraData : register(t3);
#define size cameraData[0]       // field cols/rows, supersampled width/height
#define terrain cameraData[1]    // zScale, landSpan, water, supersample
#define projection cameraData[2] // sinPitch, cosPitch, focal, centreY
#define direction cameraData[3]  // dirX, dirY, rightX, rightY
#define camera cameraData[4]     // camX, camY, camZ, baseZ
#define march cameraData[5]      // near, far, aboveWater, fogNear
#define drapeInfo cameraData[6]  // drape width/height, present, fogSpan
#define target cameraData[7]     // output width/height, unused
StructuredBuffer<uint> heights : register(t0);
StructuredBuffer<uint> drape : register(t1);
StructuredBuffer<uint> frame : register(t2);
RWStructuredBuffer<uint> output : register(u0);

uint Pack(uint3 c) { return c.x | (c.y << 8) | (c.z << 16); }
uint3 Unpack(uint c) { return uint3(c & 255, (c >> 8) & 255, (c >> 16) & 255); }

float SampleHeight(float2 p)
{
    uint2 cell = (uint2)p;
    float2 f = p - cell;
    uint a = cell.y * (uint)size.x + cell.x, b = a + (uint)size.x;
    float top = (float)heights[a] + ((float)heights[a + 1] - heights[a]) * f.x;
    float bottom = (float)heights[b] + ((float)heights[b + 1] - heights[b]) * f.x;
    return top + (bottom - top) * f.y;
}

float Slope(float2 p)
{
    p = clamp(p, float2(1, 1), size.xy - 2.001);
    float dx = (SampleHeight(p + float2(1, 0)) - SampleHeight(p - float2(1, 0))) * 0.5 * terrain.x;
    float dy = (SampleHeight(p + float2(0, 1)) - SampleHeight(p - float2(0, 1))) * 0.5 * terrain.x;
    float lambert = (dx * 0.55 - dy * 0.55 + 0.63) / sqrt(dx * dx + dy * dy + 1);
    return clamp(0.55 + lambert * 0.75, 0.30, 1.35);
}

uint3 Colour(float h, float2 p, bool sea)
{
    if (drapeInfo.z != 0)
    {
        uint tx = min((uint)drapeInfo.x - 1, (uint)(p.x * drapeInfo.x / size.x));
        uint ty = (uint)drapeInfo.y - 1 - min((uint)drapeInfo.y - 1, (uint)(p.y * drapeInfo.y / size.y));
        uint3 c = Unpack(drape[ty * (uint)drapeInfo.x + tx]);
        return sea ? c : (uint3)min(255.0, (float3)c * Slope(p));
    }
    if (sea)
    {
        float d = saturate((terrain.z - h) / max(1.0, terrain.z));
        return (uint3)(float3(64, 96, 132) - float3(26, 38, 44) * d);
    }
    float t = saturate((h - terrain.z) / terrain.y);
    float3 c = t < 0.10 ? float3(116, 146, 86)
        : t < 0.28 ? float3(92, 124, 68)
        : t < 0.48 ? float3(140, 128, 84)
        : t < 0.70 ? float3(128, 112, 98) : float3(232, 234, 238);
    return (uint3)clamp(c * Slope(p), 0.0, 255.0);
}

[numthreads(64, 1, 1)]
void Columns(uint3 id : SV_DispatchThreadID)
{
    uint sx = id.x, sw = (uint)size.z, sh = (uint)size.w;
    if (sx >= sw) return;
    for (uint y = 0; y < sh; y++)
    {
        float t = (float)y / max(1.0, size.w - 1);
        output[y * sw + sx] = Pack((uint3)(float3(26, 32, 44) + float3(32, 36, 42) * t));
    }
    float lateral = (sx - size.z * 0.5) / projection.z;
    int floorRow = sh;
    [loop]
    for (float z = march.x; z < march.y && floorRow > 0;)
    {
        float depth = z * projection.y + march.z * projection.x;
        float2 p = camera.xy + direction.xy * z + direction.zw * lateral * depth;
        float zs = z;
        z += clamp(depth * depth / (projection.z * march.z), 0.35, max(2.0, z * 0.01));
        if (any(p < 0) || any(p >= size.xy - 1)) continue;
        float h = SampleHeight(p);
        bool sea = h <= terrain.z;
        float worldZ = (sea ? terrain.z : h) * terrain.x;
        float dF = zs * projection.y + (camera.z - worldZ) * projection.x;
        if (dF < 1) continue;
        int row = (int)(projection.w - projection.z * (zs * projection.x + (worldZ - camera.z) * projection.y) / dF);
        if (row >= floorRow) continue;
        row = max(0, row);
        bool cut = floorRow >= (int)sh;
        uint3 c = cut ? uint3(78, 66, 56) : Colour(h, p, sea);
        float fog = saturate((dF - march.w) / drapeInfo.w);
        fog *= fog;
        c = (uint3)((float3)c + (float3(26, 32, 44) - c) * fog * 0.75);
        int bottom = floorRow;
        if (cut)
        {
            float bF = zs * projection.y + (camera.z - camera.w) * projection.x;
            int baseRow = bF < 1 ? floorRow
                : (int)(projection.w - projection.z * (zs * projection.x + (camera.w - camera.z) * projection.y) / bF);
            bottom = min(bottom, baseRow);
        }
        int face = bottom - row;
        for (int y = row; y < bottom; y++)
        {
            float drop = face > 3 ? (float)(y - row) / face : 0;
            output[y * sw + sx] = Pack((uint3)((float3)c * (1 - drop * 0.45)));
        }
        floorRow = row;
    }
}

[numthreads(8, 8, 1)]
void Resolve(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)target.x || id.y >= (uint)target.y) return;
    uint ss = (uint)terrain.w;
    uint3 total = 0;
    for (uint oy = 0; oy < ss; oy++)
        for (uint ox = 0; ox < ss; ox++)
            total += Unpack(frame[(id.y * ss + oy) * (uint)size.z + id.x * ss + ox]);
    output[id.y * (uint)target.x + id.x] = Pack(total / (ss * ss));
}
