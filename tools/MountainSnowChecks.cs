using Ck3MapGen.AppGUI;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Tools;

/// <summary>Checks climate coverage and the actual terrain detail textures in disposable fixtures.</summary>
internal static class MountainSnowChecks
{
    public static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "Ck3MapGen-snow-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cfg = new MapConfig { Width = 64, Height = 32, WorldWidth = 128, WorldHeight = 64 };
            var snow = MountainSnow.FromConfig(cfg);
            Check(snow.Automatic && snow.TemperaturePeak(5) == 0 && snow.TemperaturePeak(-3) == 1,
                "Default automatic temperature endpoints");
            Check(Math.Abs(snow.TemperaturePeak(1) - 0.5) < 1e-8, "Smooth temperature midpoint");
            Check(Math.Abs(snow.MoistureStrength(0) - 0.6) < 1e-8 && snow.MoistureStrength(1000) == 1,
                "Approved precipitation endpoints");
            var view = new SettingsView(cfg) { Section = "06 Map Objects" };
            Check(view.GetProperties()[nameof(cfg.MountainSnowSummerStartC)] is not null
                && view.GetProperties()[nameof(cfg.MountainSnowStartPercentile)] is null, "Auto editor rows");
            cfg.MountainSnowMode = MountainSnowMode.Manual;
            Check(view.GetProperties()[nameof(cfg.MountainSnowStartPercentile)] is not null
                && view.GetProperties()[nameof(cfg.MountainSnowSummerStartC)] is null, "Manual editor rows");
            cfg.MountainSnowMode = MountainSnowMode.Auto;
            Directory.CreateDirectory(root);
            string preset = Path.Combine(root, "preset.json");
            cfg.MountainSnowSummerStartC = 7; cfg.MountainSnowPrecipitationInfluence = 0.2;
            Preset.Save(cfg, preset);
            var loaded = new MapConfig(); Preset.Load(loaded, preset);
            Check(loaded.MountainSnowMode == MountainSnowMode.Auto && loaded.MountainSnowSummerStartC == 7
                && loaded.MountainSnowPrecipitationInfluence == 0.2, "Preset round trip");
            cfg.MountainSnowSummerStartC = 5; cfg.MountainSnowPrecipitationInfluence = 0.4;

            int n = cfg.ProvinceWidth * cfg.ProvinceHeight;
            var terrain = Enumerable.Repeat(TerrainClass.Mountains, n).ToArray();
            var zones = Enumerable.Repeat(KoppenClass.TropicalRainforest, n).ToArray();
            var elevation = Enumerable.Range(0, n).Select(i =>
                (float)(120 + 230 * Math.Pow(Math.Sin(i % cfg.ProvinceWidth * Math.PI / cfg.ProvinceWidth), 2))).ToArray();
            double Write(string name, float warm, float rain)
            {
                float[] Fill(float v) => Enumerable.Repeat(v, n).ToArray();
                var field = new ClimateField
                {
                    Width = cfg.ProvinceWidth, Height = cfg.ProvinceHeight,
                    MeanC = Fill(warm - 5), WarmC = Fill(warm), ColdC = Fill(warm - 10),
                    AnnualMm = Fill(rain), SummerMm = Fill(rain / 2), WinterMm = Fill(rain / 2), LatitudeDeg = Fill(0),
                };
                string dir = Path.Combine(root, name);
                TerrainTextureWriter.WriteAll(dir, cfg, terrain, zones, elevation, field, new Rng(92));
                var index = File.ReadAllBytes(Path.Combine(dir, "gfx", "map", "terrain", "detail_index.tga"));
                var intensity = File.ReadAllBytes(Path.Combine(dir, "gfx", "map", "terrain", "detail_intensity.tga"));
                long total = 0;
                for (int i = 18; i < index.Length; i++) if (index[i] == 46) total += intensity[i];
                return total;
            }
            double warmPaint = Write("warm", 20, 1000);
            double coldWet = Write("cold-wet", -10, 1000);
            double coldDry = Write("cold-dry", -10, 0);
            Check(warmPaint == 0 && coldWet > 0, "Actual textures: warm ground bare, cold tropical summits snowy");
            Check(coldDry > 0 && coldDry < coldWet, "Actual textures: dryness reduces snow gently");
            cfg.MountainSnowStartPercentile = 0.1; cfg.MountainSnowFullPercentile = 0.2;
            Check(Write("auto-ignores-percentiles", -10, 1000) == coldWet, "Auto ignores manual percentile controls");
            cfg.MountainSnowAmount = 0;
            Check(Write("off", -10, 1000) == 0, "Amount zero disables painted mountain snow");
            cfg.MountainSnowAmount = 1; cfg.MountainSnowMode = MountainSnowMode.Manual;
            Check(Write("manual-tropical", -10, 1000) == 0, "Manual retains original tropical exclusion");

            cfg.MountainSnowSummerStartC = -3; cfg.MountainSnowSummerFullC = 5;
            Check(MountainSnow.FromConfig(cfg).TemperaturePeak(-10) == 1, "Reversed endpoints ordered");
            cfg.MountainSnowSummerStartC = 2; cfg.MountainSnowSummerFullC = 2;
            Check(MountainSnow.FromConfig(cfg).TemperaturePeak(3) == 0
                && MountainSnow.FromConfig(cfg).TemperaturePeak(1) == 1, "Equal endpoints are a finite step");
            cfg.MountainSnowAmount = double.NaN; cfg.MountainSnowPrecipitationInfluence = double.PositiveInfinity;
            snow = MountainSnow.FromConfig(cfg);
            Check(snow.Amount == 1 && snow.PrecipitationInfluence == 0.4, "Invalid values fall back safely");
            Console.WriteLine("PASS: mountain snow climate rules, editor modes, presets and emitted detail textures.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void Check(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
    }
}
