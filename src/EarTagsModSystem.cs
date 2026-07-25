using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace EarTags
{
    public class EarTagAnchor
    {
        public string Bone;
        public double[] Offset = new double[] { 0, 0, 0 };
        public double[] Rotation = new double[] { 0, 0, 0 };
        public double Scale = 1.0;
    }

    public class EarTagsConfig
    {
        public bool MetalTagProtectsAnimals = true;
        public bool TagMessages = false;
        public bool RequireChiselForEarTags = false;
    }


    public class EarTagSpeciesConfig
    {
        public string Match;
        public string Shape;
        public string ShapeMetal;
        public string Terms;
        public EarTagAnchor Left;
        public EarTagAnchor Right;

        public EarTagAnchor ForSide(string side)
        {
            return side == EarTagsModSystem.SideLeft ? Left : Right;
        }

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
        public const string AttrTree = "eartags";
        public const string SideLeft = "left";
        public const string SideRight = "right";
        public const string AttrKindSuffix = "kind";
        public const string KindLeather = "eartag";
        public const string KindMetal = "eartagmetal";
        public const string AttrStatusTree = "animalstatus";
        public const string StatusTamed = "tamed";
        public const string TamedWild = "wild";
        public const string TamedTaming = "taming";
        public const string TamedTame = "tame";
        public const string DefaultShape = "eartags:shapes/entity/eartag.json";
        public const string DefaultShapeMetal = "eartags:shapes/entity/eartag-metal.json";
        public const string DefaultTerms = "eartag";

        public static readonly string[] Sides = new string[] { SideLeft, SideRight };

        private static readonly string[] TamedStates = new string[] { TamedWild, TamedTaming, TamedTame };

        private EarTagSpeciesConfig[] speciesConfigs = new EarTagSpeciesConfig[0];
        private EarTagsConfig settings = new EarTagsConfig();

        public bool MetalTagProtects { get { return settings == null || settings.MetalTagProtectsAnimals; } }
        public bool TagMessages { get { return settings != null && settings.TagMessages; } }
        public bool RequireChisel { get { return settings != null && settings.RequireChiselForEarTags; } }


        public static string ReadTamedState(Entity entity)
        {
            ITreeAttribute tree = entity?.WatchedAttributes?.GetTreeAttribute(AttrStatusTree);
            if (tree == null) return null;

            string state = tree.GetString(StatusTamed);

            for (int i = 0; i < TamedStates.Length; i++)
            {
                if (TamedStates[i] == state) return state;
            }

            return null;
        }


        public static AssetLocation MaterialTexture(string kind, string material)
        {
            return kind == KindMetal
                ? new AssetLocation("game", "block/metal/ingot/" + material)
                : new AssetLocation("game", "block/leather/" + material);
        }


        public static string MaterialName(string kind, string material)
        {
            return Lang.Get("eartags:" + (kind == KindMetal ? "metal-" : "color-") + material);
        }


        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            api.RegisterItemClass("ItemEarTag", typeof(ItemEarTag));
            api.RegisterEntityBehaviorClass("eartaggable", typeof(EntityBehaviorEarTaggable));
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            // Re-read settings after all mods have finished AssetsFinalize, so ConfigLib's
            // patches to settings.json are visible by the time we deserialize.
            api.Event.SaveGameLoaded += () =>
            {
                ReadSettings(api);
                api.Logger.Notification("[eartags] Settings loaded: metalProtects={0}, requireChisel={1}, tagMessages={2}",
                    settings.MetalTagProtectsAnimals, settings.RequireChiselForEarTags, settings.TagMessages);
            };
        }


        private void ReadSettings(ICoreAPI api)
        {
            try
            {
                IAsset asset = api.Assets.TryGet(new AssetLocation("eartags:config/settings.json"));
                if (asset != null) settings = asset.ToObject<EarTagsConfig>() ?? new EarTagsConfig();
            }
            catch (Exception e)
            {
                api.Logger.Warning("[eartags] Could not read settings.json, using defaults: {0}", e.Message);
                settings = new EarTagsConfig();
            }
        }


        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);

            ReadSettings(api);

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


        public EarTagSpeciesConfig ResolveAnchors(string entityCodePath)
        {
            for (int i = 0; i < speciesConfigs.Length; i++)
            {
                EarTagSpeciesConfig cfg = speciesConfigs[i];
                if (cfg?.Match != null && MatchesWildcard(cfg.Match, entityCodePath)) return cfg;
            }

            return null;
        }


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

            if (!pattern.EndsWith("*") && pos != text.Length) return false;

            return true;
        }
    }
}
