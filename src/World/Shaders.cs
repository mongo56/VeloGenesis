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
varying vec3 wpos;
varying vec3 wnorm;

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
    ALBEDO = base_color;
    ROUGHNESS = 0.8;
    SPECULAR = 0.3;
    EMISSION = lc * lines * glow * (0.2 + 0.8 * fade);
}
");

    public static readonly Shader Road = Make(@"
shader_type spatial;
render_mode cull_disabled;
uniform vec3 edge_color : source_color = vec3(0.1, 0.9, 1.0);
uniform vec3 accent_color : source_color = vec3(1.0, 0.2, 0.8);

// Anti-aliased stripe: never thinner than a pixel, dimmed instead so far-away lines don't alias.
float band(float x, float center, float half_w) {
    float fw = max(fwidth(x), 1e-5);
    float dw = max(half_w, fw);
    return (1.0 - smoothstep(dw - fw * 0.75, dw + fw * 0.75, abs(x - center))) * (half_w / dw);
}

void fragment() {
    float u = UV.x;
    float v = UV.y;
    float d = min(u, 1.0 - u);
    float edge = band(d, 0.0, 0.03);
    float inner = band(d, 0.09, 0.005);
    float dash = band(u, 0.5, 0.006) * step(0.45, fract(v / 9.0));
    float cross_line = band(fract(v / 4.0), 0.5, 0.012) * 0.12;
    float pulse = pow(fract(v / 70.0 - TIME * 0.6), 14.0);
    ALBEDO = vec3(0.008, 0.008, 0.018);
    METALLIC = 0.15;
    ROUGHNESS = 0.6;
    SPECULAR = 0.25;
    EMISSION = edge_color * (edge * (1.5 + pulse * 4.0) + inner * 0.6 + cross_line)
             + accent_color * dash * 1.1;
}
");

    public static readonly Shader Wall = Make(@"
shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never, shadows_disabled;
uniform vec3 color : source_color = vec3(0.1, 0.9, 1.0);
uniform float intensity = 0.9;
void fragment() {
    float h = clamp(UV.y, 0.0, 1.0);
    float body = pow(1.0 - h, 2.2) * 0.35;
    float top = smoothstep(0.88, 1.0, h) * 1.4;
    float scan = 0.75 + 0.25 * sin(UV.x * 0.5 - TIME * 5.0);
    ALBEDO = color * intensity * (body * scan + top);
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

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

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

    // Striped synthwave cloud bands low on the horizon.
    float az = atan(d.x, d.z);
    float cl = 0.5 + 0.5 * sin(az * 5.0 + TIME * 0.02) * sin(az * 11.0 - 1.3 + TIME * 0.013);
    float band = smoothstep(0.025, 0.07, h) * smoothstep(0.24, 0.11, h);
    float stripes = smoothstep(0.35, 0.95, sin(h * 130.0 + cl * 5.0) * 0.5 + 0.5);
    col = mix(col, vec3(0.22, 0.02, 0.24) * 1.4 + sun_bottom * 0.25 * exp(-ang * 3.0), band * stripes * cl * 0.6);

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
}
