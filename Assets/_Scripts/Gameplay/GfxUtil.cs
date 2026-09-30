using UnityEngine;

/// <summary>Tiny helpers for procedural prototype visuals (no art assets needed).</summary>
public static class GfxUtil
{
    static Sprite _circle;
    static Texture2D _circleTex;

    /// <summary>A soft white circle texture, generated once and shared.</summary>
    public static Texture2D CircleTex()
    {
        if (_circleTex != null) return _circleTex;
        const int R = 64;
        var tex = new Texture2D(R, R, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 c = new Vector2(R * 0.5f, R * 0.5f);
        for (int y = 0; y < R; y++)
        for (int x = 0; x < R; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / (R * 0.5f);
            float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - d)); // soft edge
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        _circleTex = tex;
        return _circleTex;
    }

    /// <summary>A soft white circle sprite, generated once and shared. Tint via SpriteRenderer.color.</summary>
    public static Sprite Circle()
    {
        if (_circle != null) return _circle;
        var tex = CircleTex();
        _circle = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
        _circle.name = "GenCircle";
        return _circle;
    }

    static Sprite _blob;
    /// <summary>A near-solid droplet sprite (sharper edge than Circle). Tint via SpriteRenderer.color.</summary>
    public static Sprite Blob()
    {
        if (_blob != null) return _blob;
        var tex = BlobTex();
        _blob = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
        _blob.name = "GenBlob";
        return _blob;
    }

    static Texture2D _blobTex;
    /// <summary>A near-solid droplet: opaque core with only a thin soft edge — reads as liquid, not fog.</summary>
    public static Texture2D BlobTex()
    {
        if (_blobTex != null) return _blobTex;
        const int R = 64;
        var tex = new Texture2D(R, R, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 c = new Vector2(R * 0.5f, R * 0.5f);
        for (int y = 0; y < R; y++)
        for (int x = 0; x < R; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / (R * 0.5f);
            // Solid out to ~0.82 of the radius, then a quick (not fuzzy) falloff to the edge.
            float a = 1f - Mathf.SmoothStep(0.82f, 1f, d);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        _blobTex = tex;
        return _blobTex;
    }

    static Material _spriteMat;
    public static Material SpriteMaterial()
    {
        if (_spriteMat != null) return _spriteMat;
        Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                 ?? Shader.Find("Sprites/Default")
                 ?? Shader.Find("Unlit/Transparent");
        _spriteMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        return _spriteMat;
    }
}
