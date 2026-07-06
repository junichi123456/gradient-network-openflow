using Godot;
using System.Collections.Generic;

namespace MysteryDungeon.Visuals;

// Single load path for every sprite in the project, real or placeholder.
//
// Non-directional callers (static props: Hub facilities/gates, dungeon
// stairs/item/trap markers) use the flat res://Assets/Sprites/{id}.png
// convention. Direction-aware callers (Entity, via FacingDirection) use
// a per-species folder instead: res://Assets/Sprites/{id}/{name}.png,
// e.g. Assets/Sprites/03/front.png or Assets/Sprites/03/1.png for
// chiqipi (Paldex #03). There is no direction-less single image inside
// that folder - a missing direction re-tries the front-facing file
// (front.png, then 1.png) before giving up, so a species with only one
// drawn pose just always shows that pose. Falls back to a flat-color
// square ImageTexture tinted with `fallbackColor` when spriteId is
// empty or no matching file exists yet.
//
// PlaceholderTexture2D was considered and rejected: in Godot 4 it
// renders fully transparent at runtime (it's an editor-only "missing
// asset" marker, not meant to actually draw anything in a running
// game), which would make every entity invisible. A solid-color
// texture keeps today's "colored square" visibility while giving
// artists the exact same GetTexture(spriteId, color) call site to
// target later.
//
// Sprite files are loaded via Image.Load (a raw file read), not
// ResourceLoader/GD.Load. A dropped-in PNG has no .import metadata
// until the Godot editor has opened the project at least once and
// auto-imported it - until then, ResourceLoader.Exists() reports false
// even though the file genuinely exists on disk (confirmed via
// FileAccess.FileExists returning true for the same path), so GD.Load
// would silently and permanently fall through to the placeholder.
// Image.Load reads the file directly regardless of import state, so a
// brand new asset works immediately - including in a --headless run
// that has never opened the editor, which is this project's main
// verification path.
public static class SpriteTextureLibrary
{
    private const string FlatFileFormat = "res://Assets/Sprites/{0}.png";
    private const string DirectionalFileFormat = "res://Assets/Sprites/{0}/{1}.png";

    private static readonly Vector2I FrontDirection = new(0, 1);

    private static readonly Dictionary<(Color Color, int Size), ImageTexture> _fallbackCache = new();
    private static readonly Dictionary<string, Texture2D> _imageTextureCache = new();

    public static Texture2D GetTexture(string spriteId, Color fallbackColor, int size)
    {
        if (!string.IsNullOrEmpty(spriteId))
        {
            string path = string.Format(FlatFileFormat, spriteId);
            if (TryLoadImageTexture(path, out var texture))
                return texture;
        }

        return GetFallbackTexture(fallbackColor, size);
    }

    // Direction-aware fallback for an 8-direction sprite (see Entity.
    // FacingDirection): tries every naming convention an artist might
    // reasonably use for `direction` inside the spriteId's folder (see
    // GetDirectionIdentifierCandidates), then - if none of those exist -
    // retries the same candidates for the front-facing direction, so a
    // species drawn in only one pose still renders instead of falling
    // all the way to the solid-color placeholder.
    public static Texture2D GetTexture(string spriteId, Vector2I direction, Color fallbackColor, int size)
    {
        if (!string.IsNullOrEmpty(spriteId))
        {
            foreach (string identifier in GetDirectionIdentifierCandidates(direction))
            {
                string path = string.Format(DirectionalFileFormat, spriteId, identifier);
                if (TryLoadImageTexture(path, out var texture))
                    return texture;
            }
        }

        return GetFallbackTexture(fallbackColor, size);
    }

    // Y-down = "front" (matches this project's GridToWorld convention,
    // where +Y is downward on screen). Numbers 1-8 mirror the same
    // front/back/left/right/fl/fr/bl/br order, for artists who'd rather
    // name files "5.png" than spell out "fl"/"lf".
    private static readonly Dictionary<Vector2I, string> _directionNames = new()
    {
        [new Vector2I(0, 1)] = "front",
        [new Vector2I(0, -1)] = "back",
        [new Vector2I(-1, 0)] = "left",
        [new Vector2I(1, 0)] = "right",
        [new Vector2I(-1, 1)] = "fl",
        [new Vector2I(1, 1)] = "fr",
        [new Vector2I(-1, -1)] = "bl",
        [new Vector2I(1, -1)] = "br",
    };

    private static readonly Dictionary<Vector2I, int> _directionNumbers = new()
    {
        [new Vector2I(0, 1)] = 1,
        [new Vector2I(0, -1)] = 2,
        [new Vector2I(-1, 0)] = 3,
        [new Vector2I(1, 0)] = 4,
        [new Vector2I(-1, 1)] = 5,
        [new Vector2I(1, 1)] = 6,
        [new Vector2I(-1, -1)] = 7,
        [new Vector2I(1, -1)] = 8,
    };

    // Only the 4 diagonals have a reversible two-letter name; a cardinal
    // (front/back/left/right) has no ordering to mix up.
    private static readonly Dictionary<Vector2I, string> _reversedDiagonalNames = new()
    {
        [new Vector2I(-1, 1)] = "lf",
        [new Vector2I(1, 1)] = "rf",
        [new Vector2I(-1, -1)] = "lb",
        [new Vector2I(1, -1)] = "rb",
    };

    // This direction's own name/reversed-name/number, then - only if
    // `direction` isn't already front - front's own three, as the
    // "at least draw something" fallback pose.
    private static IEnumerable<string> GetDirectionIdentifierCandidates(Vector2I direction)
    {
        foreach (string id in GetOwnIdentifiers(direction))
            yield return id;

        if (direction != FrontDirection)
            foreach (string id in GetOwnIdentifiers(FrontDirection))
                yield return id;
    }

    private static IEnumerable<string> GetOwnIdentifiers(Vector2I direction)
    {
        yield return _directionNames.GetValueOrDefault(direction, "front"); // (0,0) etc. safely default to front

        if (_reversedDiagonalNames.TryGetValue(direction, out string reversed))
            yield return reversed;

        if (_directionNumbers.TryGetValue(direction, out int number))
            yield return number.ToString();
    }

    // Direct file-system read (see the class-level comment for why this
    // is Image.Load rather than ResourceLoader/GD.Load). Cached by path
    // so repeated calls (every direction change, every entity of the
    // same species) don't reload/re-decode the same PNG from disk.
    // Deliberately doesn't cache a *missing* path, so an asset added
    // mid-session (e.g. iterating in the editor) is picked up on the
    // very next call instead of staying stuck on the placeholder.
    private static bool TryLoadImageTexture(string path, out Texture2D texture)
    {
        if (_imageTextureCache.TryGetValue(path, out texture))
            return true;

        if (!FileAccess.FileExists(path))
        {
            texture = null;
            return false;
        }

        var image = new Image();
        if (image.Load(path) != Error.Ok)
        {
            texture = null;
            return false;
        }

        texture = ImageTexture.CreateFromImage(image);
        _imageTextureCache[path] = texture;
        return true;
    }

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
