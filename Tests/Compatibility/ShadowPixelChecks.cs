using Client.MirObjects;
using Mir2.Hosting;
using Server.ConsoleHost;

internal static class ShadowPixelChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var phase in new[] { 0, 1 })
        foreach (var colour in new[] { new byte[] { 0, 0, 0 }, [8, 0, 0], [0, 4, 0], [16, 8, 8] })
        {
            var pixels = Checker(5, 5, colour, phase); var before = (byte[])pixels.Clone();
            check(OriginalShadowPixels.RestoreAlpha("Map/WemadeMir2/Objects", pixels, 5, 5) == 25, "奇数尺寸或反向点阵阴影未完整恢复");
            check(Enumerable.Range(0, 25).All(i => pixels[i * 4 + 3] == 128 && pixels.AsSpan(i * 4, 3).SequenceEqual(colour)), "阴影色值或50%覆盖率错误");
            var coverage = Enumerable.Range(0, 25).Sum(i => before[i * 4 + 3]);
            check(Math.Abs(coverage - 25 * 128) <= 255, "点阵还原明显改变了整体阴影深度");
            check(OriginalShadowPixels.RestoreAlpha("Map/WemadeMir2/Objects", pixels, 5, 5) == 0, "再次上传叠加处理已还原阴影");
        }
        foreach (var library in new[] { "CArmour/01", "AArmour/00", "ARArmour/00", "CHair/00", "CWeapon/00", "NPC/00", "Monster/000", "Pet/00", "Flag/00", "Gate/00", "Siege/00" })
            check(OriginalShadowPixels.RestoreAlpha(library, Checker(2, 2, [0, 4, 0]), 2, 2) == 4, "世界素材类别未应用阴影恢复：" + library);
        foreach (var library in new[] { "Prguse", "Title", "ChrSel", "Items", "Magic", "Effect/00", "Prguse/00" })
        {
            var pixels = Checker(4, 4, [0, 0, 0]); var before = (byte[])pixels.Clone();
            check(OriginalShadowPixels.RestoreAlpha(library, pixels, 4, 4) == 0 && pixels.SequenceEqual(before), "阴影恢复误改界面或特效素材：" + library);
        }
        var samples = new List<byte[]> { Checker(4, 4, [100, 80, 20]), Enumerable.Repeat(new byte[] { 0, 0, 0, 255 }, 16).SelectMany(p => p).ToArray() };
        var translucent = Checker(4, 4, [0, 0, 0]); for (var i = 3; i < translucent.Length; i += 4) if (translucent[i] != 0) translucent[i] = 128;
        samples.Add(translucent);
        var mismatched = Checker(2, 2, [0, 0, 0]); mismatched[12] = 8; samples.Add(mismatched);
        foreach (var sample in samples)
        {
            var before = (byte[])sample.Clone(); var side = sample.Length == 16 ? 2 : 4;
            check(OriginalShadowPixels.RestoreAlpha("Map/Test", sample, side, side) == 0 && sample.SequenceEqual(before), "实色/彩色/已有Alpha/不同色点阵被误处理");
        }
        var mixed = new byte[6 * 4 * 4]; var checker = Checker(4, 4, [8, 0, 0]);
        for (var y = 0; y < 4; y++) checker.AsSpan(y * 16, 16).CopyTo(mixed.AsSpan((y * 6 + 1) * 4));
        mixed[0] = 50; mixed[3] = 255; var outside = (byte[])mixed.Clone();
        OriginalShadowPixels.RestoreAlpha("Monster/00", mixed, 6, 4);
        check(Enumerable.Range(0, 24).Where(i => i % 6 is 0 or 5).All(i => mixed.AsSpan(i * 4, 4).SequenceEqual(outside.AsSpan(i * 4, 4))), "阴影向原轮廓外扩张或污染相邻像素");
        check(OriginalShadowPixels.RestoreAlpha("Map/Test", new byte[4], 1, 1) == 0, "单像素图错误生成阴影");
        foreach (var (width, height, count) in new[] { (0, 1, 4), (2, 2, 4), (4097, 1, 4) })
        {
            try { OriginalShadowPixels.RestoreAlpha("Map/Test", new byte[count], width, height); throw new Exception("错误尺寸未拒绝"); }
            catch (InvalidDataException) { check(true, "阴影尺寸边界"); }
        }
    }

    public static void RunAssets(ProjectPaths paths, Action<bool, string> check)
    {
        var assets = new GameAssets(paths.Assets);
        foreach (var (library, index) in new[] { ("Map/WemadeMir2/Objects", 1409), ("CArmour/01", 0), ("NPC/00", 0), ("Monster/000", 0) })
        {
            var frame = assets.Frame(library, index); var image = SpritePreview.Decode(frame); var before = (byte[])image.Pixels.Clone();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var changed = OriginalShadowPixels.RestoreAlpha(library, image.Pixels, image.Width, image.Height);
            Console.WriteLine($"Shadow {library}/{index}: {image.Width}x{image.Height}, restored={changed}, CPU={watch.Elapsed.TotalMilliseconds:F3}ms");
            check(changed > 0 || library.StartsWith("Monster/"), "截图对应的真实素材没有识别到点阵阴影：" + library);
            check(Enumerable.Range(0, before.Length / 4).All(i => before[i * 4 + 3] is 0 or 255 && before[i * 4] <= 16 && before[i * 4 + 1] <= 16 && before[i * 4 + 2] <= 16 ||
                before.AsSpan(i * 4, 4).SequenceEqual(image.Pixels.AsSpan(i * 4, 4))), "真实素材中非点阵像素被改变：" + library);
            check(assets.Frame(library, index).SequenceEqual(frame), "原素材或缓存字节被修改：" + library);
        }
    }

    private static byte[] Checker(int width, int height, byte[] colour, int phase = 0)
    {
        var result = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            if ((x + y + phase) % 2 == 0) { var offset = (y * width + x) * 4; colour.CopyTo(result, offset); result[offset + 3] = 255; }
        return result;
    }
}
