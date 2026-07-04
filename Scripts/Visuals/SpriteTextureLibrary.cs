using Godot;
using System.Collections.Generic;

namespace MysteryDungeon.Visuals;

// Single load path for every sprite in the project, real or placeholder:
// tries res://Assets/Sprites/{spriteId}.png first (the eventual
// MagicaVoxel-rendered art drop-in point - just add the PNG there and
// set SpriteId, no code changes needed), and falls back to a flat-color
// square ImageTexture tinted with `fallbackColor` when spriteId is
// empty or the file doesn't exist yet.
//
// PlaceholderTexture2D was considered and rejected: in Godot 4 it
// renders fully transparent at runtime (it's an editor-only "missing
// asset" marker, not meant to actually draw anything in a running
// game), which would make every entity invisible. A solid-color
// texture keeps today's "colored square" visibility while giving
// artists the exact same GetTexture(spriteId, color) call site to
// target later.
public static class SpriteTextureLibrary
{
    private const string AssetPathFormat = "res://Assets/Sprites/{0}.png";
    private static readonly Dictionary<(Color Color, int Size), ImageTexture> _fallbackCache = new();

    public static Texture2D GetTexture(string spriteId, Color fallbackColor, int size)
    {
        if (!string.IsNullOrEmpty(spriteId))
        {
            string path = string.Format(AssetPathFormat, spriteId);
            if (ResourceLoader.Exists(path))
                return GD.Load<Texture2D>(path);
        }

        return GetFallbackTexture(fallbackColor, size);
    }

    // 3-tier fallback for an 8-direction-aware sprite (see Entity.
    // FacingDirection): a per-direction image (e.g. "hinokojika_front.png")
    // takes priority, then the direction-less single image, then the
    // solid-color placeholder - reuses the 3-arg overload above for the
    // last two tiers so there's exactly one place that knows the
    // placeholder policy.
    public static Texture2D GetTexture(string spriteId, Vector2I direction, Color fallbackColor, int size)
    {
        if (!string.IsNullOrEmpty(spriteId))
        {
            string directionalPath = string.Format(AssetPathFormat, spriteId + GetDirectionSuffix(direction));
            if (ResourceLoader.Exists(directionalPath))
                return GD.Load<Texture2D>(directionalPath);
        }

        return GetTexture(spriteId, fallbackColor, size);
    }

    // Y-down = "front" (matches this project's GridToWorld convention,
    // where +Y is downward on screen). Any direction outside this set
    // (only (0,0) in practice, which Entity.UpdateFacingDirection never
    // actually produces) safely falls back to "_front".
    private static string GetDirectionSuffix(Vector2I direction) => (direction.X, direction.Y) switch
    {
        (0, 1) => "_front",
        (0, -1) => "_back",
        (-1, 0) => "_left",
        (1, 0) => "_right",
        (-1, 1) => "_fl",
        (1, 1) => "_fr",
        (-1, -1) => "_bl",
        (1, -1) => "_br",
        _ => "_front",
    };

    private static Texture2D GetFallbackTexture(Color color, int size)
    {
        var key = (color, size);
        if (_fallbackCache.TryGetValue(key, out var cached))
            return cached;

        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(color);
        var texture = ImageTexture.CreateFromImage(image);

        _fallbackCache[key] = texture;
        return texture;
    }
}
