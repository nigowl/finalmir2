namespace Client.MirObjects;

public static class OriginalShadowPixels
{
    public static int RestoreAlpha(string library, byte[] pixels, int width, int height)
    {
        var slash = library.IndexOf('/');
        if (slash < 0 || library[..slash] is not ("Map" or "CArmour" or "AArmour" or "ARArmour" or
            "CHair" or "AHair" or "ARHair" or "CWeapon" or "AWeapon" or "ARWeapon" or
            "NPC" or "Monster" or "Pet" or "Flag" or "Gate" or "Siege")) return 0;
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096 || pixels.Length != (long)width * height * 4)
            throw new InvalidDataException("素材阴影像素尺寸无效。");

        byte[] mask = null;
        for (var y = 0; y < height - 1; y++)
        for (var x = 0; x < width - 1; x++)
        {
            var a = y * width + x; var b = a + 1; var c = a + width; var d = c + 1;
            var diagonal = Dark(pixels, a) && Dark(pixels, d) && SameColour(pixels, a, d) &&
                pixels[b * 4 + 3] == 0 && pixels[c * 4 + 3] == 0;
            var opposite = !diagonal && Dark(pixels, b) && Dark(pixels, c) && SameColour(pixels, b, c) &&
                pixels[a * 4 + 3] == 0 && pixels[d * 4 + 3] == 0;
            if (!diagonal && !opposite) continue;
            // 只还原明确的同色深色/透明对角点阵；保留普通实色、现有Alpha及美术细节。
            mask ??= new byte[width * height];
            mask[a] = mask[d] = diagonal ? (byte)1 : (byte)2;
            mask[b] = mask[c] = diagonal ? (byte)2 : (byte)1;
        }
        if (mask == null) return 0;
        var changed = 0;
        for (var pixel = 0; pixel < mask.Length; pixel++)
        {
            if (mask[pixel] == 0) continue;
            if (mask[pixel] == 2)
            {
                var source = pixel % width > 0 && mask[pixel - 1] == 1 ? pixel - 1 :
                    pixel % width + 1 < width && mask[pixel + 1] == 1 ? pixel + 1 :
                    pixel >= width && mask[pixel - width] == 1 ? pixel - width : pixel + width;
                pixels[pixel * 4] = pixels[source * 4];
                pixels[pixel * 4 + 1] = pixels[source * 4 + 1];
                pixels[pixel * 4 + 2] = pixels[source * 4 + 2];
            }
            pixels[pixel * 4 + 3] = 128;
            changed++;
        }
        return changed;
    }

    private static bool Dark(byte[] pixels, int pixel)
    {
        var offset = pixel * 4;
        return pixels[offset + 3] == 255 && pixels[offset] <= 16 && pixels[offset + 1] <= 16 && pixels[offset + 2] <= 16;
    }

    private static bool SameColour(byte[] pixels, int a, int b) => pixels[a * 4] == pixels[b * 4] &&
        pixels[a * 4 + 1] == pixels[b * 4 + 1] && pixels[a * 4 + 2] == pixels[b * 4 + 2];
}
