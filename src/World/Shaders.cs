using Godot;

namespace VeloGenesis.World;

/// <summary>Shader sources for the neon grid look. Compiled once and shared.</summary>
public static class Shaders
{
    static Shader Make(string code) => new() { Code = code };

    public static readonly Shader Terrain = Make(@"
shader_type spatial;
render_mode cull_disabled;
uniform vec3 grid_color : source_color = vec3(0.1, 0.9, 1.0);
uniform vec3 accent_color : source_color = vec3(1.0, 0.2, 0.8);
uniform vec3 base_color : source_color = vec3(0.010, 0.004, 0.026);
uniform float grid_size = 12.0;
uniform float glow = 2.4;
uniform float fade_dist = 1100.0;
// 1 = shaded rock surface, 0 = plain dark base (the far horizon plane).
uniform float solid = 1.0;
uniform vec3 rock_low : source_color = vec3(0.045, 0.02, 0.085);
uniform vec3 rock_high_a : source_color = vec3(0.13, 0.045, 0.16);
uniform vec3 rock_high_b : source_color = vec3(0.2, 0.07, 0.12);
varying vec3 wpos;
varying vec3 wnorm;
" + NoiseLib + @"

void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

// Anti-aliased line in one axis ('pristine grid'): the line has a real width in grid units; when it
// gets thinner than a pixel it is drawn one pixel wide but dimmed, so it converges to its average
// brightness in the distance instead of breaking up into jaggies and shimmer.
float aa_line(float uv, float width) {
    float deriv = max(length(vec2(dFdx(uv), dFdy(uv))), 1e-6);
    float draw = clamp(width, deriv, 0.5);
    float aa = deriv * 1.5;
    float g = 1.0 - abs(fract(uv) * 2.0 - 1.0);
    float l = smoothstep(draw + aa, draw - aa, g);
    l *= clamp(width / draw, 0.0, 1.0);
    return mix(l, width, clamp(deriv * 2.0 - 1.0, 0.0, 1.0));
}

float grid_line(vec2 c, float width) {
    float lx = aa_line(c.x, width);
    float ly = aa_line(c.y, width);
    return mix(lx, 1.0, ly);
}

void fragment() {
    float dist = length(VERTEX);
    float minor = grid_line(wpos.xz / grid_size, 0.014);
    float major = grid_line(wpos.xz / (grid_size * 5.0), 0.007);
    float contour = aa_line(wpos.y / 7.0, 0.03);
    float steep = smoothstep(0.92, 0.6, abs(wnorm.y));
    float fade = exp(-dist / fade_dist);
    float lines = max(minor * 0.5, major) * (1.0 - steep * 0.7) + contour * steep * 0.9;
    vec3 lc = mix(grid_color, accent_color, clamp(steep * 0.8, 0.0, 1.0));

    // Solid rock under the grid: dusky violet valleys rising to warmer, sun-catching strata on the
    // high slopes, with noise so the faces don't read as flat polygons.
    float n = vnoise(wpos.xz / 18.0) * 0.6 + vnoise(wpos.xz / 4.0) * 0.4;
    float strata = 0.5 + 0.5 * sin(wpos.y / 3.2 + n * 3.0);
    float high = smoothstep(30.0, 160.0, UV.x);
    vec3 low_col = rock_low;
    vec3 high_col = mix(rock_high_a, rock_high_b, strata);
    ALBEDO = mix(low_col, high_col, clamp(high * 0.7 + steep * 0.5, 0.0, 1.0)) * (0.75 + 0.5 * n) * solid
           + base_color * (1.0 - solid);
    ROUGHNESS = 0.85;
    SPECULAR = 0.3;
    // Faint violet sky-glow on silhouettes keeps far ridges separate from each other.
    float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
    EMISSION = lc * lines * glow * (0.2 + 0.8 * fade) + vec3(0.18, 0.04, 0.22) * rim * 0.35 * solid;
}
");

    // Value noise shared by the surface shaders.
    const string NoiseLib = @"
float hash12(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash12(i), hash12(i + vec2(1, 0)), u.x), mix(hash12(i + vec2(0, 1)), hash12(i + vec2(1, 1)), u.x), u.y);
}
";

    public static readonly Shader Road = Make(@"
shader_type spatial;
render_mode cull_disabled;
uniform vec3 edge_color : source_color = vec3(0.1, 0.9, 1.0);
uniform vec3 accent_color : source_color = vec3(1.0, 0.2, 0.8);
// Lead-in only: the road dissolves into the grid before this ride distance.
uniform float fade_from = -1e9;
uniform float fade_len = 1.0;
varying vec3 wpos;
" + NoiseLib + @"
void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

// Anti-aliased stripe: never thinner than a pixel, dimmed instead so far-away lines don't alias.
float band(float x, float center, float half_w) {
    float fw = max(fwidth(x), 1e-5);
    float dw = max(half_w, fw);
    return (1.0 - smoothstep(dw - fw * 0.75, dw + fw * 0.75, abs(x - center))) * (half_w / dw);
}

vec2 hash22(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.xx + p3.yz) * p3.zy);
}

// Pebbles: distance to the nearest stone centre (x) and that stone's random id (y).
vec2 stones(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    float md = 8.0, id = 0.0;
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++) {
        vec2 g = vec2(float(x), float(y));
        vec2 r = g + hash22(i + g) * 0.8 + 0.1 - f;
        float d = dot(r, r);
        if (d < md) { md = d; id = hash12(i + g); }
    }
    return vec2(sqrt(md), id);
}

void fragment() {
    float u = UV.x;
    float v = UV.y;
    float vis = clamp((v - fade_from) / fade_len, 0.0, 1.0);
    if (vis < 1.0 && hash12(floor(FRAGCOORD.xy)) > vis * vis) discard;
    float d = min(u, 1.0 - u);
    float edge = band(d, 0.0, 0.03);
    float inner = band(d, 0.09, 0.005);
    float pulse = pow(fract(v / 70.0 - TIME * 0.6), 14.0);

    // Gravel track: two packed tyre ruts per lane, loose stones on the crown and the verges.
    vec2 p = wpos.xz;
    float dist = length(VERTEX);
    float detail = clamp(1.0 - dist / 45.0, 0.0, 1.0);
    float lane = fract(u * 2.0);
    float rut = smoothstep(0.16, 0.04, abs(lane - 0.3)) + smoothstep(0.16, 0.04, abs(lane - 0.72));
    rut = clamp(rut, 0.0, 1.0) * (0.7 + 0.3 * vnoise(p * 0.5));
    float loose = 1.0 - rut * 0.8;

    vec2 big = stones(p * 9.0);
    vec2 small = stones(p * 23.0 + 7.3);
    float pebble = smoothstep(0.46, 0.26, big.x) * step(0.35 + rut * 0.4, big.y);
    float grit = smoothstep(0.45, 0.2, small.x);
    // Height field for bump shading: domed pebbles over fine grit.
    float h = (pebble * (1.0 - big.x * 1.6) * 1.0 + grit * 0.35) * loose;

    vec3 dirt = mix(vec3(0.1, 0.1, 0.11), vec3(0.06, 0.06, 0.07), rut) * (0.8 + 0.4 * vnoise(p * 1.7));
    vec3 stone_col = mix(vec3(0.24, 0.235, 0.24), vec3(0.1, 0.1, 0.11), big.y) * (0.85 + 0.3 * grit);
    vec3 gravel = mix(dirt, stone_col, pebble * loose);
    gravel = mix(vec3(0.1, 0.1, 0.11) * (0.85 + 0.3 * vnoise(p * 1.7)), gravel, detail);

    // A few muddy puddles still catch the neon and the sunset.
    float puddle = smoothstep(0.6, 0.7, vnoise(p * 0.11) * 0.65 + vnoise(p * 0.43) * 0.35) * (0.4 + 0.6 * rut);
    ALBEDO = mix(gravel, vec3(0.02, 0.016, 0.02), puddle);
    ROUGHNESS = mix(mix(0.92, 0.7, rut), 0.12, puddle);
    SPECULAR = 0.4;
    METALLIC = 0.0;
    AO = mix(mix(0.5, 0.25, pebble * loose), 1.0, puddle);
    AO_LIGHT_AFFECT = 0.0;

    // Bump from the height field (surface-gradient bump mapping in view space).
    vec3 dpdx = dFdx(VERTEX), dpdy = dFdy(VERTEX);
    vec3 r1 = cross(dpdy, NORMAL), r2 = cross(NORMAL, dpdx);
    float det = dot(dpdx, r1);
    vec3 grad = sign(det) * (dFdx(h) * r1 + dFdy(h) * r2);
    float strength = 0.02 * detail * (1.0 - puddle);
    NORMAL = normalize(abs(det) * NORMAL - grad * strength);

    float spill = exp(-d * 16.0) * (0.04 + 0.16 * puddle) * (1.0 + pulse * 2.0);
    EMISSION = edge_color * (edge * (1.5 + pulse * 4.0) + inner * 0.6 + spill + vis * (1.0 - vis) * 1.6);
}
");

    public static readonly Shader Wall = Make(@"
shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never, shadows_disabled;
uniform vec3 color : source_color = vec3(0.1, 0.9, 1.0);
uniform float intensity = 0.9;
uniform float fade_from = -1e9;
uniform float fade_len = 1.0;
void fragment() {
    float h = clamp(UV.y, 0.0, 1.0);
    float vis = clamp((UV.x - fade_from) / fade_len, 0.0, 1.0);
    float body = pow(1.0 - h, 2.2) * 0.35;
    float top = smoothstep(0.88, 1.0, h) * 1.4;
    float scan = 0.75 + 0.25 * sin(UV.x * 0.5 - TIME * 5.0);
    ALBEDO = color * intensity * (body * scan + top) * vis;
}
");

    public static readonly Shader Crystal = Make(@"
shader_type spatial;
varying vec3 wpos;
varying vec3 icolor;
uniform float glow = 2.2;
void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    icolor = COLOR.rgb;
}
void fragment() {
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    float bands = smoothstep(0.88, 1.0, fract(wpos.y / 5.0 - TIME * 0.04));
    ALBEDO = vec3(0.01, 0.01, 0.02);
    METALLIC = 0.8;
    ROUGHNESS = 0.18;
    EMISSION = icolor * (fres * glow + bands * 0.9 + 0.04);
}
");

    public static readonly Shader Gel = Make(@"
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_always;
uniform vec3 color : source_color = vec3(1.0, 0.85, 0.25);
// 1 = rider is in the gel zone (bright, collectable); 0 = faint ghost you ride through.
uniform float active = 1.0;
varying float vy;
void vertex() {
    vy = VERTEX.y;
    float a = TIME * (1.0 + active * 1.6) + float(INSTANCE_ID) * 1.3;
    mat2 r = mat2(vec2(cos(a), sin(a)), vec2(-sin(a), cos(a)));
    VERTEX.xz = r * VERTEX.xz;
    NORMAL.xz = r * NORMAL.xz;
    VERTEX.y += sin(TIME * 3.0 + float(INSTANCE_ID)) * 0.12 * active;
}
void fragment() {
    float fres = pow(1.0 - abs(dot(NORMAL, VIEW)), 2.0);
    float tab = smoothstep(0.3, 0.34, vy);                       // tear-off top
    float label = (1.0 - smoothstep(0.03, 0.05, abs(vy + 0.02))) * 0.9; // glowing band
    vec3 c = mix(color, vec3(1.0, 1.0, 0.85), tab * 0.6 + label);
    ALBEDO = c * mix(0.22, 2.6, active) * (0.55 + 0.7 * fres);
    ALPHA = mix(0.3, 1.0, active);
}
");

    public static readonly Shader Ghost = Make(@"
shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never, shadows_disabled;
uniform vec3 color : source_color = vec3(0.55, 0.95, 1.0);
void fragment() {
    float f = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 1.8);
    float scan = 0.75 + 0.25 * sin(FRAGCOORD.y * 0.6 + TIME * 9.0);
    ALBEDO = color * (0.07 + f * 1.3) * scan;
}
");

    public static readonly Shader Rim = Make(@"
shader_type spatial;
uniform vec3 rim_color : source_color = vec3(0.1, 0.9, 1.0);
uniform vec3 body_color : source_color = vec3(0.02, 0.02, 0.035);
uniform float rim = 2.6;
uniform float metallic = 0.7;
uniform float roughness = 0.3;
void fragment() {
    float f = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.4);
    ALBEDO = body_color;
    METALLIC = metallic;
    ROUGHNESS = roughness;
    EMISSION = rim_color * f * rim;
}
");

    public static readonly Shader Trail = Make(@"
shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never, shadows_disabled;
uniform vec3 color : source_color = vec3(0.1, 0.9, 1.0);
uniform float boost = 1.0;
void fragment() {
    float age = clamp(UV.x, 0.0, 1.0);
    float h = clamp(UV.y, 0.0, 1.0);
    float fade = pow(1.0 - age, 1.6);
    float top = smoothstep(0.8, 1.0, h);
    ALBEDO = color * fade * (0.05 + top * 1.0) * 0.32 * boost;
}
");

    public static readonly Shader Sky = Make(@"
shader_type sky;
uniform vec3 top_color : source_color = vec3(0.012, 0.0, 0.05);
uniform vec3 horizon_color : source_color = vec3(0.38, 0.03, 0.36);
uniform vec3 sun_top : source_color = vec3(1.0, 0.86, 0.25);
uniform vec3 sun_bottom : source_color = vec3(1.0, 0.12, 0.55);
uniform vec3 sun_dir = vec3(0.0, 0.1, -1.0);
uniform float sun_size = 0.17;
uniform vec3 planet_dir = vec3(0.7, 0.35, 0.6);
uniform vec3 cloud_dark : source_color = vec3(0.07, 0.01, 0.12);
// Biome extras: skyline under the sun (0..1) and aurora curtains (0..1).
uniform float city = 1.0;
uniform float aurora = 0.0;
uniform vec3 aurora_color : source_color = vec3(0.2, 1.0, 0.6);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
" + NoiseLib + @"
void sky() {
    vec3 d = EYEDIR;
    float h = d.y;
    vec3 col = mix(horizon_color, top_color, pow(clamp(h * 2.2, 0.0, 1.0), 0.55));
    if (h < 0.0) col = mix(horizon_color * 0.4, vec3(0.0), clamp(-h * 5.0, 0.0, 1.0));

    // Stars
    vec2 sp = vec2(atan(d.x, d.z), asin(clamp(d.y, -1.0, 1.0))) * 160.0;
    vec2 cell = floor(sp);
    float r = hash(cell);
    if (r > 0.986) {
        vec2 f = fract(sp) - 0.5;
        float s = smoothstep(0.12, 0.0, length(f));
        col += vec3(0.75, 0.82, 1.0) * s * (0.55 + 0.45 * sin(TIME * 2.0 + r * 60.0)) * clamp(h * 4.0, 0.0, 1.0) * 1.4;
    }

    // Striped synthwave sun
    vec3 sd = normalize(sun_dir);
    float ang = acos(clamp(dot(d, sd), -1.0, 1.0));
    float v = (d.y - sd.y) / sun_size;
    if (ang < sun_size) {
        vec3 sc = mix(sun_bottom, sun_top, clamp(v * 0.5 + 0.55, 0.0, 1.0));
        float stripe = 1.0;
        if (v < 0.15) {
            float k = fract(v * 6.0 + TIME * 0.12);
            float w = mix(0.06, 0.5, clamp(0.15 - v, 0.0, 1.0));
            stripe = step(w, k);
        }
        float edge = smoothstep(sun_size, sun_size * 0.985, ang);
        col = mix(col, sc * 3.2, edge * stripe);
    }
    col += sun_bottom * 0.35 * exp(-ang * 5.0);

    // Ringed planet hanging in the sky.
    vec3 pd = normalize(planet_dir);
    float pa = acos(clamp(dot(d, pd), -1.0, 1.0));
    const float pr = 0.075;
    vec3 pright = normalize(cross(pd, vec3(0.0, 1.0, 0.0)));
    vec3 pup = cross(pright, pd);
    vec2 lp = vec2(dot(d - pd, pright), dot(d - pd, pup)) / pr;
    vec2 rl = vec2(lp.x, lp.y * 3.4 + lp.x * 0.55);
    float rr = length(rl);
    float ring = smoothstep(1.45, 1.52, rr) * smoothstep(2.3, 2.2, rr) * (0.55 + 0.45 * sin(rr * 38.0));
    col += vec3(0.5, 0.15, 0.8) * exp(-max(pa - pr, 0.0) * 35.0) * 0.3;
    bool ringFront = rl.y < 0.0;
    if (!ringFront) col += vec3(0.95, 0.5, 1.0) * ring * 0.9;
    if (pa < pr) {
        vec3 sn = vec3(lp, sqrt(max(0.0, 1.0 - dot(lp, lp))));
        float lit = clamp(dot(sn, normalize(vec3(-0.6, 0.45, 0.65))), 0.0, 1.0);
        vec3 pc = mix(vec3(0.03, 0.0, 0.09), vec3(0.75, 0.25, 0.95), lit);
        pc += vec3(0.3, 0.05, 0.4) * sin(lp.y * 14.0 + lp.x * 2.0) * 0.12 * lit;
        pc += vec3(0.9, 0.4, 1.0) * pow(1.0 - sn.z, 4.0) * 0.6;
        col = mix(col, pc, smoothstep(pr, pr * 0.97, pa));
    }
    if (ringFront) col += vec3(0.95, 0.5, 1.0) * ring * 0.9;

    float az = atan(d.x, d.z);

    // Soft halo around the sun.
    col += sun_top * 0.12 * exp(-ang * 14.0) + sun_bottom * 0.08 * exp(-ang * 2.2);

    // Long streaky sunset clouds on a curved layer: bright gold-pink undersides near the sun,
    // fading to dark violet away from it. They pass in front of the sun.
    if (h > 0.0) {
        vec2 cp = d.xz / (h + 0.12);
        cp = vec2(cp.x * 0.35, cp.y * 1.6) + vec2(TIME * 0.004, 0.0);
        float c = vnoise(cp * 1.3) * 0.55 + vnoise(cp * 3.1) * 0.3 + vnoise(cp * 7.9) * 0.15;
        float cover = smoothstep(0.52, 0.75, c) * smoothstep(0.015, 0.06, h) * smoothstep(0.42, 0.14, h);
        float sun_lit = exp(-ang * 2.4);
        vec3 cloud = mix(cloud_dark, sun_bottom * 1.1 + sun_top * 0.35, sun_lit);
        cloud += sun_top * pow(sun_lit, 3.0) * smoothstep(0.6, 0.85, c) * 1.6; // silver lining
        col = mix(col, cloud, cover * 0.85);
    }

    // Distant megacity skyline under the sun, with lit windows.
    float sun_az = atan(sd.x, sd.z);
    float ra = mod(az - sun_az + 3.14159, 6.28318) - 3.14159;
    float cityw = smoothstep(1.25, 0.5, abs(ra)) * city;
    if (cityw > 0.001 && h < 0.06 && h > -0.02) {
        float bx = ra * 70.0;
        float b1 = (0.004 + pow(hash(vec2(floor(bx), 3.0)), 2.2) * 0.03) * cityw;
        float bx2 = ra * 140.0 + 0.37;
        float b2 = (0.002 + pow(hash(vec2(floor(bx2), 9.0)), 3.0) * 0.045) * cityw;
        // Thin antenna on a few of the tallest blocks.
        float spire = step(0.93, hash(vec2(floor(bx2), 5.0))) * step(abs(fract(bx2) - 0.5), 0.04) * 0.02 * cityw;
        float top = max(max(b1, b2), b2 + spire);
        if (h < top) {
            float front = step(b1, b2);
            float wx = front > 0.5 ? bx2 * 6.0 : bx * 6.0;
            vec2 wc = vec2(floor(wx), floor(h * 900.0));
            float win = step(0.25, fract(wx)) * step(fract(h * 900.0), 0.55) * step(0.78, hash(wc));
            vec3 wcol = mix(vec3(1.0, 0.25, 0.8), vec3(0.3, 0.9, 1.0), step(0.5, hash(wc + 13.0)));
            wcol = mix(wcol, vec3(1.0, 0.7, 0.3), step(0.85, hash(wc + 29.0)));
            vec3 body = mix(vec3(0.03, 0.005, 0.06), vec3(0.06, 0.01, 0.09), front);
            float edge_lit = smoothstep(top - 0.0015, top, h);
            col = body + wcol * win * 1.6 + sun_bottom * edge_lit * 0.6;
            if (spire > 0.0 && h > b2) col += vec3(1.0, 0.2, 0.3) * step(top - 0.002, h) * (0.6 + 0.4 * sin(TIME * 4.0)) * 3.0;
        }
    }

    // Aurora: folded curtains with a bright wavy lower edge fading upwards (green to violet),
    // in slowly drifting patches, away from the sun.
    if (aurora > 0.001 && h > 0.0) {
        float away = smoothstep(0.5, 1.6, abs(ra));
        float base = 0.1 + vnoise(vec2(az * 1.6 + TIME * 0.015, 3.0)) * 0.14;
        float above = h - base;
        float curtain = smoothstep(-0.004, 0.02, above) * exp(-max(above, 0.0) * 6.0);
        float patches = smoothstep(0.3, 0.75, vnoise(vec2(az * 3.0 - TIME * 0.02, 7.0)));
        float rays = 0.65 + 0.35 * vnoise(vec2(az * 70.0, TIME * 0.25));
        float a = curtain * patches * rays * away * aurora;
        vec3 ac = mix(aurora_color, vec3(0.7, 0.3, 1.0), smoothstep(0.02, 0.22, above));
        col += ac * a * 1.5;
    }

    // Occasional shooting star.
    float period = 6.5;
    float idx = floor(TIME / period);
    float tt = fract(TIME / period);
    if (tt < 0.12 && h > 0.15) {
        vec2 s0 = vec2(hash(vec2(idx, 1.0)) * 6.283 - 3.14159, 0.35 + hash(vec2(idx, 2.0)) * 0.6);
        vec2 dir = normalize(vec2(hash(vec2(idx, 3.0)) - 0.5, -0.35));
        float prog = tt / 0.12;
        vec2 head = s0 + dir * prog * 0.5;
        vec2 tail = head - dir * 0.09;
        vec2 p = vec2(az, asin(clamp(d.y, -1.0, 1.0)));
        vec2 pa2 = p - tail, ba = head - tail;
        float hh = clamp(dot(pa2, ba) / dot(ba, ba), 0.0, 1.0);
        float dist = length(pa2 - ba * hh);
        col += vec3(0.9, 0.85, 1.0) * smoothstep(0.003, 0.0, dist) * hh * (1.0 - prog) * 3.0;
    }

    col += horizon_color * 1.3 * exp(-abs(h) * 45.0);
    COLOR = col;
}
");

    public static readonly Shader Palm = Make(@"
shader_type spatial;
render_mode cull_disabled;
uniform float frond = 0.0;
varying vec3 icolor;
void vertex() {
    icolor = COLOR.rgb;
    if (frond > 0.5) {
        float s = sin(TIME * 1.3 + float(INSTANCE_ID) * 1.7) * 0.025 * UV.y;
        VERTEX.x += s;
        VERTEX.z += s * 0.6;
    }
}
void fragment() {
    ALBEDO = vec3(0.01, 0.008, 0.02);
    ROUGHNESS = 0.7;
    if (frond > 0.5) {
        float edge = smoothstep(0.1, 0.0, min(UV.x, 1.0 - UV.x)) + smoothstep(0.035, 0.0, abs(UV.x - 0.5)) * 0.7;
        EMISSION = icolor * (edge * 2.2 + 0.06);
    } else {
        float ring = smoothstep(0.8, 1.0, fract(UV.y * 14.0));
        EMISSION = icolor * (ring * 1.5 + 0.05);
    }
}
");

    public static readonly Shader Building = Make(@"
shader_type spatial;
varying vec3 wpos;
varying vec3 icolor;
varying vec3 lnorm;
float hash(vec2 p) { return fract(sin(dot(p, vec2(41.3, 289.1))) * 15731.743); }
void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    icolor = COLOR.rgb;
    lnorm = NORMAL;
}
void fragment() {
    vec2 c = abs(lnorm.x) > 0.5 ? vec2(wpos.z, wpos.y) : vec2(wpos.x, wpos.y);
    vec2 g = c / vec2(3.5, 4.5);
    vec2 cell = floor(g);
    vec2 f = fract(g);
    float win = step(0.18, f.x) * step(f.x, 0.82) * step(0.25, f.y) * step(f.y, 0.7);
    float lit = step(0.7, hash(cell));
    float flicker = 0.85 + 0.15 * sin(TIME * 3.0 + hash(cell + 7.0) * 40.0);
    float roof = abs(lnorm.y) > 0.5 ? 1.0 : 0.0;
    float dist = length(VERTEX);
    float far_fade = clamp(1.0 - dist / 2500.0, 0.3, 1.0);
    ALBEDO = vec3(0.008, 0.006, 0.018);
    ROUGHNESS = 0.6;
    EMISSION = icolor * (win * lit * flicker * 1.5 * (1.0 - roof) + roof * 0.25) * far_fade;
}
");

    public static readonly Shader Floater = Make(@"
shader_type spatial;
varying vec3 icolor;
void vertex() {
    float id = float(INSTANCE_ID);
    float a = TIME * (0.2 + fract(id * 0.37) * 0.4) + id;
    mat2 r = mat2(vec2(cos(a), sin(a)), vec2(-sin(a), cos(a)));
    VERTEX.xz = r * VERTEX.xz;
    NORMAL.xz = r * NORMAL.xz;
    VERTEX.y += sin(TIME * 0.6 + id * 2.1) * 0.08;
    icolor = COLOR.rgb;
}
void fragment() {
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.5);
    ALBEDO = vec3(0.01, 0.01, 0.02);
    METALLIC = 0.9;
    ROUGHNESS = 0.15;
    EMISSION = icolor * (fres * 2.4 + 0.12);
}
");

    public static readonly Shader Beam = Make(@"
shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never, shadows_disabled;
varying float ly;
varying vec3 icolor;
void vertex() {
    ly = VERTEX.y + 0.5;
    icolor = COLOR.rgb;
}
void fragment() {
    float side = pow(abs(dot(NORMAL, VIEW)), 2.0);
    float fade = pow(1.0 - clamp(ly, 0.0, 1.0), 1.6);
    float pulse = 0.75 + 0.25 * sin(TIME * 2.0 - ly * 12.0);
    ALBEDO = icolor * side * fade * pulse * 0.9;
}
");

    /// <summary>Full-screen finishing pass over the 3D view: film grain, edge chromatic aberration, vignette.</summary>
    public static readonly Shader PostFx = Make(@"
shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float grain = 0.028;
uniform float aberration = 0.0035;
uniform float vignette = 0.42;
void fragment() {
    vec2 uv = SCREEN_UV;
    vec2 c = uv - 0.5;
    float r2 = dot(c, c);
    vec2 off = c * aberration * r2 * 4.0;
    vec3 col = vec3(texture(screen_tex, uv + off).r, texture(screen_tex, uv).g, texture(screen_tex, uv - off).b);
    float n = fract(sin(dot(FRAGCOORD.xy + fract(TIME * 7.3) * vec2(173.0, 91.0), vec2(12.9898, 78.233))) * 43758.5453);
    // Grain is strongest in the mid-tones, like film.
    float lum = dot(col, vec3(0.299, 0.587, 0.114));
    col += (n - 0.5) * grain * (0.4 + 1.2 * lum * (1.0 - lum));
    col *= 1.0 - vignette * smoothstep(0.08, 0.5, r2);
    COLOR = vec4(col, 1.0);
}
");

    /// <summary>Glass signboard: dark tinted panel, glowing chamfered frame, scanlines, light sweep.</summary>
    public static readonly Shader HoloPanel = Make(@"
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;
uniform vec3 accent : source_color = vec3(0.1, 0.9, 1.0);
uniform vec2 size = vec2(8.0, 3.0);
uniform float cut = 0.55;
void fragment() {
    vec2 p = vec2(UV.x, 1.0 - UV.y) * size;
    float tl = p.x + (size.y - p.y);
    float br = (size.x - p.x) + p.y;
    if (tl < cut || br < cut) discard;
    float d = min(min(p.x, size.x - p.x), min(p.y, size.y - p.y));
    d = min(d, min(tl - cut, br - cut) * 0.7071);
    float fw = fwidth(p.x) * 1.2;
    float frame = 1.0 - smoothstep(0.04, 0.04 + fw, d);
    float inner = (1.0 - smoothstep(0.012, 0.012 + fw, abs(d - 0.14))) * 0.35;
    float scan = 0.5 + 0.5 * sin(p.y * 55.0 - TIME * 2.5);
    float x = (p.x + p.y * 0.6) / (size.x + size.y * 0.6);
    float sweep = exp(-pow((x - fract(TIME * 0.11) * 1.8 + 0.4) * 9.0, 2.0));
    vec3 body = vec3(0.02, 0.008, 0.05) + accent * (0.035 + 0.04 * p.y / size.y);
    body += accent * (scan * 0.018 + sweep * 0.07);
    ALBEDO = mix(body, accent * 2.6, max(frame, inner));
    ALPHA = mix(0.82, 1.0, frame);
}
");

    /// <summary>Emissive trim whose colour comes from the MultiMesh instance colour.</summary>
    public static readonly Shader InstanceGlow = Make(@"
shader_type spatial;
uniform float energy = 2.6;
void fragment() {
    ALBEDO = vec3(0.01, 0.01, 0.02);
    ROUGHNESS = 0.4;
    EMISSION = COLOR.rgb * energy;
}
");

    /// <summary>Faceted dark stone with faint glowing fissures in the instance colour.</summary>
    public static readonly Shader Rock = Make(@"
shader_type spatial;
varying vec3 wpos;
varying vec3 icolor;
" + NoiseLib + @"
void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    icolor = COLOR.rgb;
}
void fragment() {
    float n = vnoise(wpos.xz * 0.35 + wpos.y * 0.2) * 0.6 + vnoise(wpos.xy * 1.3) * 0.4;
    float strata = 0.5 + 0.5 * sin(wpos.y * 1.7 + n * 4.0);
    ALBEDO = mix(vec3(0.05, 0.022, 0.08), vec3(0.16, 0.06, 0.15), strata * 0.6 + n * 0.4);
    ROUGHNESS = 0.75 - strata * 0.2;
    SPECULAR = 0.4;
    float v = vnoise(wpos.xz * 0.18 + vec2(wpos.y * 0.15));
    float fw = fwidth(v) * 1.5 + 1e-4;
    float vein = 1.0 - smoothstep(0.012, 0.012 + fw, abs(v - 0.5));
    float dist = length(VERTEX);
    EMISSION = icolor * vein * 1.8 * clamp(1.0 - dist / 600.0, 0.0, 1.0);
}
");

    /// <summary>Matte painted bike frame with a thin glowing pinstripe in the route colour.</summary>
    public static readonly Shader Paint = Make(@"
shader_type spatial;
uniform vec3 base_color : source_color = vec3(0.42, 0.46, 0.39);
uniform vec3 color : source_color = vec3(0.1, 0.9, 1.0);
uniform float energy = 1.6;
void fragment() {
    float stripe = 1.0 - smoothstep(0.012, 0.03, abs(fract(UV.x * 2.0 + 0.25) - 0.5));
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    ALBEDO = base_color;
    METALLIC = 0.15;
    ROUGHNESS = 0.5;
    CLEARCOAT = 0.3;
    CLEARCOAT_ROUGHNESS = 0.4;
    EMISSION = color * (stripe * energy + fres * 0.12);
}
");

    /// <summary>Gravel tyre (torus around local Y): knobbly black tread and tan-wall sidewalls.</summary>
    public static readonly Shader Tyre = Make(@"
shader_type spatial;
varying vec3 lpos;
varying vec3 lnorm;
void vertex() {
    lpos = VERTEX;
    lnorm = NORMAL;
}
void fragment() {
    float side = smoothstep(0.45, 0.7, abs(lnorm.y));
    float ang = atan(lpos.z, lpos.x);
    float knob = step(0.5, fract(ang * 52.0 / 6.2832 + step(0.0, lpos.y) * 0.5)) * (1.0 - side);
    ALBEDO = mix(vec3(0.025, 0.022, 0.02) * (1.0 + knob * 0.6), vec3(0.42, 0.26, 0.13), side);
    ROUGHNESS = mix(0.8, 0.6, side);
    SPECULAR = 0.3;
}
");

    /// <summary>
    /// Clothing / skin split along the part's local Y: colour_a below <c>split</c>, colour_b above,
    /// with a faint coloured rim so the rider stays readable against the dark world.
    /// </summary>
    public static readonly Shader Kit = Make(@"
shader_type spatial;
uniform vec3 color_a : source_color = vec3(0.8, 0.6, 0.45);
uniform vec3 color_b : source_color = vec3(0.8, 0.6, 0.45);
uniform float split = 100.0;
uniform float rough_a = 0.6;
uniform float rough_b = 0.6;
uniform vec3 rim_color : source_color = vec3(0.1, 0.9, 1.0);
uniform float rim = 0.35;
varying float ly;
void vertex() {
    ly = VERTEX.y;
}
void fragment() {
    float b = step(split, ly);
    ALBEDO = mix(color_a, color_b, b);
    ROUGHNESS = mix(rough_a, rough_b, b);
    SPECULAR = 0.35;
    float f = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    EMISSION = rim_color * f * rim;
}
");
}
