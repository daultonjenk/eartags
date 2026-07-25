using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

// NOTE: This mod is compiled by the game as a source mod, and that compiler does not reference the
// System.Collections facade assembly. Dictionary<,> therefore cannot be used anywhere in this mod
// (CS0012). Arrays only - see attachpoints.json being an array rather than an object.

namespace EarTags
{
    /// <summary>Placement of the tag on one ear of one species.</summary>
    public class EarTagAnchor
    {
        /// <summary>Name of the bone in the animal's shape to step-parent the tag onto.</summary>
        public string Bone;
        public double[] Offset = new double[] { 0, 0, 0 };
        public double[] Rotation = new double[] { 0, 0, 0 };
        public double Scale = 1.0;
    }

    /// <summary>
    /// Settings that live outside the assets, in ModConfig/eartags.json, because they are the
    /// server operator's business rather than the mod's.
    /// </summary>
    public class EarTagsConfig
    {
        /// <summary>
        /// Whether applying and removing a tag announces itself in chat. Off by default: tagging a
        /// flock one animal at a time would bury the chat, and the leather sound already confirms
        /// the tag went on. Turn it on with .eartagmessages while working out why a tag will not
        /// stick.
        ///
        /// Only the success lines are covered. Refusals - both ears full, species not supported -
        /// are always sent, because those fire once and only when something did not work, and a
        /// silent refusal is indistinguishable from a broken mod.
        /// </summary>
        public bool TagMessages = false;

        /// <summary>
        /// Whether the hover panel shows tags as coloured swatches on one line instead of naming
        /// them on one line and the protection on another. Two lines become one and the colours
        /// read at a glance, which is the whole point of tagging an animal.
        ///
        /// Turn it off if the swatches come out as literal &lt;font&gt; markup - that would mean the
        /// entity info panel does not parse VTML on this version, and the wordy lines still work.
        /// Client side setting: it only affects what you see.
        /// </summary>
        public bool VisualInfo = true;
    }


    public class EarTagSpeciesConfig
    {
        /// <summary>Wildcard matched against the entity code path, e.g. "sheep-mouflon-adult-*".</summary>
        public string Match;

        /// <summary>
        /// Attachment shape step-parented onto the bone. Null means the ear tag plate; a chicken
        /// points this at the legband instead.
        /// </summary>
        public string Shape;

        /// <summary>
        /// The same shape for a metal tag. A separate file rather than a flag because the only
        /// difference is the per-face reflectiveMode that gives metal its shine, and faces live in
        /// a Dictionary&lt;,&gt; that a source mod cannot reach into.
        /// </summary>
        public string ShapeMetal;

        /// <summary>
        /// Lang key prefix for the chat lines and the info text, so a chicken can say "leg band"
        /// where a sheep says "ear tag". Null means <see cref="EarTagsModSystem.DefaultTerms"/>.
        /// </summary>
        public string Terms;

        /// <summary>
        /// Whether ".eartags nudge z" flips the sign on the right side. True where Z is a hang
        /// direction that the ear's mirrored rotationX has turned round, false where it is just a
        /// centring offset into the thickness of the ear - see attachpoints.json.
        /// </summary>
        public bool MirrorZ = true;

        public EarTagAnchor Left;
        public EarTagAnchor Right;

        public EarTagAnchor ForSide(string side)
        {
            return side == EarTagsModSystem.SideLeft ? Left : Right;
        }

        /// <summary>Which shape file a tag of this kind hangs on this species.</summary>
        public string ShapeFor(string kind)
        {
            if (kind == EarTagsModSystem.KindMetal)
            {
                return string.IsNullOrEmpty(ShapeMetal) ? EarTagsModSystem.DefaultShapeMetal : ShapeMetal;
            }

            return string.IsNullOrEmpty(Shape) ? EarTagsModSystem.DefaultShape : Shape;
        }

        public string TermsOrDefault
        {
            get { return string.IsNullOrEmpty(Terms) ? EarTagsModSystem.DefaultTerms : Terms; }
        }
    }


    public class EarTagsModSystem : ModSystem
    {
        /// <summary>WatchedAttributes subtree holding this animal's tags.</summary>
        public const string AttrTree = "eartags";
        public const string SideLeft = "left";
        public const string SideRight = "right";

        /// <summary>
        /// Suffix on the side key for the kind of tag worn, e.g. "leftkind". Written alongside the
        /// material rather than folded into it so that saves made before metal tags existed still
        /// read correctly - an absent kind means leather.
        /// </summary>
        public const string AttrKindSuffix = "kind";

        /// <summary>Item code prefixes, which double as the tag kind stored per side.</summary>
        public const string KindLeather = "eartag";
        public const string KindMetal = "eartagmetal";

        /// <summary>Attachment shapes used by any species that does not name one of its own.</summary>
        public const string DefaultShape = "eartags:shapes/entity/eartag.json";
        public const string DefaultShapeMetal = "eartags:shapes/entity/eartag-metal.json";

        /// <summary>Lang key prefix used by any species that does not name one of its own.</summary>
        public const string DefaultTerms = "eartag";

        public static readonly string[] Sides = new string[] { SideLeft, SideRight };

        private const string SettingsFile = "eartags.json";

        private EarTagSpeciesConfig[] speciesConfigs = new EarTagSpeciesConfig[0];
        private EarTagsConfig settings = new EarTagsConfig();

        public EarTagSpeciesConfig[] SpeciesConfigs { get { return speciesConfigs; } }
        public EarTagsConfig Settings { get { return settings; } }


        /// <summary>
        /// Whether to announce a tag going on or coming off. Read through the mod system rather
        /// than a static so that a dedicated server and an integrated one behave the same way.
        /// </summary>
        public bool TagMessages { get { return settings != null && settings.TagMessages; } }

        public bool VisualInfo { get { return settings == null || settings.VisualInfo; } }


        /// <summary>Reads ModConfig/eartags.json, writing a default one if it is missing.</summary>
        public void LoadSettings(ICoreAPI api)
        {
            try
            {
                settings = api.LoadModConfig<EarTagsConfig>(SettingsFile);

                if (settings == null)
                {
                    settings = new EarTagsConfig();
                    api.StoreModConfig(settings, SettingsFile);
                }
            }
            catch (Exception e)
            {
                // A typo in the config must not take the mod down with it.
                api.Logger.Warning("[eartags] Could not read {0}, using defaults: {1}", SettingsFile, e.Message);
                settings = new EarTagsConfig();
            }
        }


        public void SaveSettings(ICoreAPI api)
        {
            try
            {
                api.StoreModConfig(settings, SettingsFile);
            }
            catch (Exception e)
            {
                api.Logger.Warning("[eartags] Could not write {0}: {1}", SettingsFile, e.Message);
            }
        }


        /// <summary>
        /// Where a tag of this kind gets its colour from. Metal borrows the ingot art rather than
        /// the plate art, matching what vanilla metalplate does with its own texture and covering
        /// every variant of the block/metal property.
        /// </summary>
        public static AssetLocation MaterialTexture(string kind, string material)
        {
            return kind == KindMetal
                ? new AssetLocation("game", "block/metal/ingot/" + material)
                : new AssetLocation("game", "block/leather/" + material);
        }


        /// <summary>The material's name for chat and info text - "copper", "red".</summary>
        public static string MaterialName(string kind, string material)
        {
            return Lang.Get("eartags:" + (kind == KindMetal ? "metal-" : "color-") + material);
        }


        /// <summary>
        /// Swatch colour for the hover panel, roughly matching the dye or the ingot it is cut from.
        /// A switch rather than a config file: thirty-three fixed values that nobody tunes twice,
        /// and a switch cannot be a Dictionary by accident (see the note at the top of this file).
        /// </summary>
        public static string MaterialHex(string material)
        {
            switch (material)
            {
                // dyed leather
                case "orange": return "#d2762a";
                case "black":  return "#2b2b2b";
                case "red":    return "#a83232";
                case "blue":   return "#3a5fa8";
                case "purple": return "#7a4a9e";
                case "pink":   return "#d97fa8";
                case "white":  return "#e8e2d5";
                case "yellow": return "#d9c02e";
                case "gray":   return "#8a8a8a";
                case "green":  return "#4a8a3c";

                // metals
                case "bismuth":        return "#b0a8b8";
                case "bismuthbronze":  return "#a08a5e";
                case "blackbronze":    return "#6b5a4a";
                case "brass":          return "#c9a227";
                case "chromium":       return "#b8bcc0";
                case "copper":         return "#c07a3e";
                case "cupronickel":    return "#c4b5a0";
                case "electrum":       return "#d9c87a";
                case "gold":           return "#e0b83c";
                case "iron":           return "#a8a29a";
                case "lead":           return "#6e6e78";
                case "meteoriciron":   return "#7a7168";
                case "molybdochalkos": return "#8a7a5a";
                case "nickel":         return "#b5b2a5";
                case "platinum":       return "#d0d4d8";
                case "silver":         return "#cdd1d4";
                case "stainlesssteel": return "#c0c4c6";
                case "steel":          return "#9ea3a8";
                case "tin":            return "#cfd2d6";
                case "tinbronze":      return "#b08a4a";
                case "titanium":       return "#b6bcc2";
                case "uranium":        return "#7e8a5e";
                case "zinc":           return "#b9c0c4";
            }

            // An unknown material still gets a swatch, just a neutral one.
            return "#999999";
        }


        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            api.RegisterItemClass("ItemEarTag", typeof(ItemEarTag));
            api.RegisterEntityBehaviorClass("eartaggable", typeof(EntityBehaviorEarTaggable));
        }


        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            // The hover panel is drawn client side, so the client needs the settings too. Mod
            // configs are deliberately not synced - which suits these, since one is about what the
            // server says and the other about what you see.
            LoadSettings(api);
            EarTagsCommands.RegisterClient(api, this);
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            // Messages are sent server side, so the server is the only side that needs the setting.
            LoadSettings(api);
            EarTagsCommands.RegisterServer(api, this);
        }


        /// <summary>
        /// Full path to the live config file on disk. Mod.SourcePath points at the mod folder for
        /// an unpacked mod - if this mod is ever zipped, in-game reload and save stop working and
        /// you are back to editing plus a world reload.
        /// </summary>
        public string ConfigDiskPath()
        {
            if (Mod?.SourcePath == null) return null;
            return Mod.SourcePath + "/assets/eartags/config/attachpoints.json";
        }


        /// <summary>Re-reads attachpoints.json straight off disk. Returns the entry count, or -1.</summary>
        public int ReloadFromDisk(ICoreAPI api)
        {
            try
            {
                string path = ConfigDiskPath();
                if (path == null || !System.IO.File.Exists(path))
                {
                    api.Logger.Warning("[eartags] Config not found on disk at {0}", path);
                    return -1;
                }

                // JsonUtil lives in Vintagestory.API.Common, not .Util
                EarTagSpeciesConfig[] loaded = JsonUtil.ToObject<EarTagSpeciesConfig[]>(
                    System.IO.File.ReadAllText(path), "eartags");

                if (loaded == null) return -1;

                speciesConfigs = loaded;
                return loaded.Length;
            }
            catch (Exception e)
            {
                api.Logger.Warning("[eartags] Could not reload config: {0}", e.Message);
                return -1;
            }
        }


        /// <summary>
        /// Writes the current offsets/scale back into attachpoints.json by rewriting only the
        /// left:/right: lines. Everything else in the file - comments especially - is left alone,
        /// since this file is meant to stay hand-editable.
        /// </summary>
        public bool SaveToDisk(ICoreAPI api)
        {
            try
            {
                string path = ConfigDiskPath();
                if (path == null || !System.IO.File.Exists(path)) return false;

                string[] lines = System.IO.File.ReadAllLines(path);
                EarTagSpeciesConfig current = null;

                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();

                    if (trimmed.StartsWith("match:"))
                    {
                        current = MatchingConfigForLine(trimmed);
                        continue;
                    }

                    if (current == null) continue;

                    if (IsKeyLine(trimmed, "left")) lines[i] = FormatAnchorLine("left", current.Left, true);
                    else if (IsKeyLine(trimmed, "right")) lines[i] = FormatAnchorLine("right", current.Right, false);
                }

                System.IO.File.WriteAllLines(path, lines);
                return true;
            }
            catch (Exception e)
            {
                api.Logger.Warning("[eartags] Could not save config: {0}", e.Message);
                return false;
            }
        }


        /// <summary>
        /// Whether an already-left-trimmed line is "key:", allowing space before the colon. The
        /// padding matters: FormatAnchorLine writes the key padded out to line the two anchors up,
        /// so a matcher that insisted on "left:" would find the hand-written file and then fail to
        /// find its own output, and every save after the first would silently do nothing.
        /// </summary>
        private static bool IsKeyLine(string trimmed, string key)
        {
            if (!trimmed.StartsWith(key, StringComparison.Ordinal)) return false;

            int i = key.Length;
            while (i < trimmed.Length && (trimmed[i] == ' ' || trimmed[i] == '\t')) i++;

            return i < trimmed.Length && trimmed[i] == ':';
        }


        private EarTagSpeciesConfig MatchingConfigForLine(string trimmedMatchLine)
        {
            int first = trimmedMatchLine.IndexOf('"');
            if (first < 0) return null;

            int last = trimmedMatchLine.IndexOf('"', first + 1);
            if (last < 0) return null;

            string pattern = trimmedMatchLine.Substring(first + 1, last - first - 1);

            for (int i = 0; i < speciesConfigs.Length; i++)
            {
                if (speciesConfigs[i]?.Match == pattern) return speciesConfigs[i];
            }

            return null;
        }


        private static string FormatAnchorLine(string side, EarTagAnchor a, bool isLeft)
        {
            if (a == null) return "\t\t" + side + ": null,";

            return string.Format(
                "\t\t{0}: {{ bone: \"{1}\", offset: [ {2}, {3}, {4} ], rotation: [ {5}, {6}, {7} ], scale: {8} }}{9}",
                side.PadRight(6), a.Bone,
                N(a.Offset, 0), N(a.Offset, 1), N(a.Offset, 2),
                N(a.Rotation, 0), N(a.Rotation, 1), N(a.Rotation, 2),
                a.Scale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                isLeft ? "," : "");
        }


        private static string N(double[] arr, int i)
        {
            double v = arr != null && arr.Length > i ? arr[i] : 0;
            return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }


        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);

            try
            {
                IAsset asset = api.Assets.TryGet(new AssetLocation("eartags:config/attachpoints.json"));

                if (asset == null)
                {
                    api.Logger.Warning("[eartags] attachpoints.json not found, no animal will render tags.");
                    return;
                }

                speciesConfigs = asset.ToObject<EarTagSpeciesConfig[]>() ?? new EarTagSpeciesConfig[0];

                api.Logger.Notification("[eartags] Loaded tag placement for {0} species pattern(s).", speciesConfigs.Length);
            }
            catch (Exception e)
            {
                api.Logger.Error("[eartags] Could not read attachpoints.json, tags will not render: {0}", e);
            }
        }


        /// <summary>Finds the placement config whose wildcard matches this entity's code, or null.</summary>
        public EarTagSpeciesConfig ResolveAnchors(string entityCodePath)
        {
            for (int i = 0; i < speciesConfigs.Length; i++)
            {
                EarTagSpeciesConfig cfg = speciesConfigs[i];
                if (cfg?.Match != null && MatchesWildcard(cfg.Match, entityCodePath)) return cfg;
            }

            return null;
        }


        /// <summary>
        /// Minimal '*' glob match. Deliberately hand-rolled rather than pulled from the API so the
        /// mod does not depend on a WildcardUtil overload that may shift between game versions.
        /// </summary>
        public static bool MatchesWildcard(string pattern, string text)
        {
            if (pattern == null || text == null) return false;

            string[] parts = pattern.Split('*');
            int pos = 0;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0) continue;

                if (i == 0)
                {
                    if (!text.StartsWith(part, StringComparison.OrdinalIgnoreCase)) return false;
                    pos = part.Length;
                    continue;
                }

                int at = text.IndexOf(part, pos, StringComparison.OrdinalIgnoreCase);
                if (at < 0) return false;
                pos = at + part.Length;
            }

            // A pattern with no trailing '*' must consume the whole string
            if (!pattern.EndsWith("*") && pos != text.Length) return false;

            return true;
        }
    }
}
