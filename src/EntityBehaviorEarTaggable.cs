using System;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace EarTags
{
    public class EntityBehaviorEarTaggable : EntityBehavior
    {
        private ICoreClientAPI capi;
        private EarTagSpeciesConfig anchors;
        private EarTagsModSystem system;


        public EntityBehaviorEarTaggable(Entity entity) : base(entity) { }

        public override string PropertyName() { return "eartaggable"; }


        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            system = entity.Api.ModLoader.GetModSystem<EarTagsModSystem>();
            anchors = system?.ResolveAnchors(entity.Code?.Path);

            capi = entity.Api as ICoreClientAPI;

            if (capi != null)
            {
                entity.WatchedAttributes.RegisterModifiedListener(EarTagsModSystem.AttrTree, OnTagsChanged);
            }
        }


        private void OnTagsChanged()
        {
            entity.MarkShapeModified();
        }


        public string GetTag(string side)
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute(EarTagsModSystem.AttrTree);
            if (tree == null) return null;

            string material = tree.GetString(side);
            return string.IsNullOrEmpty(material) ? null : material;
        }


        public string GetTagKind(string side)
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute(EarTagsModSystem.AttrTree);
            if (tree == null) return EarTagsModSystem.KindLeather;

            string kind = tree.GetString(side + EarTagsModSystem.AttrKindSuffix);
            return string.IsNullOrEmpty(kind) ? EarTagsModSystem.KindLeather : kind;
        }


        public void SetTag(string side, string kind, string material)
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetOrAddTreeAttribute(EarTagsModSystem.AttrTree);

            if (material == null)
            {
                tree.RemoveAttribute(side);
                tree.RemoveAttribute(side + EarTagsModSystem.AttrKindSuffix);
            }
            else
            {
                tree.SetString(side, material);
                tree.SetString(side + EarTagsModSystem.AttrKindSuffix, kind ?? EarTagsModSystem.KindLeather);
            }

            entity.WatchedAttributes.MarkPathDirty(EarTagsModSystem.AttrTree);
        }


        public bool WearsMetalTag()
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute(EarTagsModSystem.AttrTree);
            if (tree == null) return false;

            for (int i = 0; i < EarTagsModSystem.Sides.Length; i++)
            {
                string side = EarTagsModSystem.Sides[i];
                string material = tree.GetString(side);
                if (string.IsNullOrEmpty(material)) continue;
                if (tree.GetString(side + EarTagsModSystem.AttrKindSuffix) == EarTagsModSystem.KindMetal) return true;
            }

            return false;
        }


        public string NextFreeSide(bool preferRight)
        {
            if (preferRight) return GetTag(EarTagsModSystem.SideRight) == null ? EarTagsModSystem.SideRight : null;

            for (int i = 0; i < EarTagsModSystem.Sides.Length; i++)
            {
                if (GetTag(EarTagsModSystem.Sides[i]) == null) return EarTagsModSystem.Sides[i];
            }

            return null;
        }


        public bool CanRenderSide(string side)
        {
            return anchors?.ForSide(side)?.Bone != null;
        }


        public string Terms
        {
            get { return anchors == null ? EarTagsModSystem.DefaultTerms : anchors.TermsOrDefault; }
        }


        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            base.OnEntityReceiveDamage(damageSource, ref damage);

            if (damageSource == null || damage <= 0) return;
            if (damageSource.Type == EnumDamageType.Heal) return;
            if (!IsPlayerCaused(damageSource)) return;
            if (system == null || !system.MetalTagProtects) return;
            if (!WearsMetalTag()) return;

            damage = 0;
        }


        private static bool IsPlayerCaused(DamageSource damageSource)
        {
            if (damageSource.Source == EnumDamageSource.Player) return true;

            return damageSource.SourceEntity is EntityPlayer || damageSource.CauseEntity is EntityPlayer;
        }


        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
        {
            base.OnTesselation(ref entityShape, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);

            if (capi == null || anchors == null) return;

            string left = GetTag(EarTagsModSystem.SideLeft);
            string right = GetTag(EarTagsModSystem.SideRight);
            if (left == null && right == null) return;

            if (!shapeIsCloned)
            {
                entityShape = entityShape.Clone();
                shapeIsCloned = true;
            }

            if (left != null) AttachTag(entityShape, EarTagsModSystem.SideLeft, left, shapePathForLogging);
            if (right != null) AttachTag(entityShape, EarTagsModSystem.SideRight, right, shapePathForLogging);
        }


        private void AttachTag(Shape entityShape, string side, string material, string shapePathForLogging)
        {
            try
            {
                EarTagAnchor anchor = anchors.ForSide(side);
                if (anchor?.Bone == null) return;

                if (entityShape.GetElementByName(anchor.Bone) == null)
                {
                    capi.Logger.Warning(
                        "[eartags] {0} has no bone named '{1}', cannot place the {2} ear tag. Check attachpoints.json against {3}.",
                        entity.Code, anchor.Bone, side, shapePathForLogging);
                    return;
                }

                string kind = GetTagKind(side);
                AssetLocation shapeLocation = new AssetLocation(anchors.ShapeFor(kind));

                Shape tagShape = LoadTagShape(shapeLocation);
                if (tagShape?.Elements == null || tagShape.Elements.Length == 0) return;

                ShapeElement el = tagShape.Elements[0];
                el.Name = "EarTag-" + side;
                el.StepParentName = anchor.Bone;
                ApplyAnchor(el, anchor);

                SetShapeTexture(tagShape, "tag", EarTagsModSystem.MaterialTexture(kind, material));

                string prefix = kind + material;

                entityShape.StepParentShape(
                    tagShape,
                    prefix,
                    shapeLocation.ToShortString(),
                    shapePathForLogging,
                    capi.Logger,
                    (code, loc) => RegisterEntityTexture(prefix + code, loc),
                    0f
                );
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[eartags] Failed to attach {0} ear tag to {1}: {2}", side, entity.Code, e);
            }
        }


        private void RegisterEntityTexture(string textureCode, AssetLocation location)
        {
            try
            {
                object holder = entity.Properties?.Client;
                if (holder == null) return;

                object dict = ReadMember(holder, "Textures");
                if (dict == null) return;

                Type dt = dict.GetType();

                object present = dt.GetMethod("ContainsKey")?.Invoke(dict, new object[] { textureCode });
                if (present is bool && (bool)present) return;

                AssetLocation texPath = new AssetLocation(location.Domain, "textures/" + location.Path + ".png");
                bool resolved = capi.Assets.TryGet(texPath) != null;

                if (!resolved)
                {
                    capi.Logger.Warning("[eartags] Asset {0} not found, falling back to {1}", texPath, location);
                    texPath = location.Clone();
                }

                int subId;
                TextureAtlasPosition texPos;

                if (!capi.EntityTextureAtlas.GetOrInsertTexture(texPath, out subId, out texPos, null, 0f))
                {
                    capi.Logger.Warning("[eartags] Atlas insert failed for {0}", texPath);
                    return;
                }

                CompositeTexture ct = new CompositeTexture(location);
                ct.Baked = new BakedCompositeTexture();
                ct.Baked.BakedName = texPath;
                ct.Baked.TextureSubId = subId;

                MethodInfo setter = dt.GetMethod("set_Item");

                if (setter == null)
                {
                    capi.Logger.Warning("[eartags] No set_Item on texture collection type {0}", dt.FullName);
                    return;
                }

                setter.Invoke(dict, new object[] { textureCode, ct });
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[eartags] Could not register texture {0}: {1}", location, e);
            }
        }


        private static void SetShapeTexture(Shape shape, string key, AssetLocation location)
        {
            object dict = ReadMember(shape, "Textures");
            if (dict == null) return;

            dict.GetType().GetMethod("set_Item")?.Invoke(dict, new object[] { key, location });
        }


        private static object ReadMember(object target, string name)
        {
            Type t = target.GetType();

            FieldInfo field = t.GetField(name);
            if (field != null) return field.GetValue(target);

            PropertyInfo prop = t.GetProperty(name);
            return prop?.GetValue(target);
        }


        private static void ApplyAnchor(ShapeElement el, EarTagAnchor anchor)
        {
            for (int i = 0; i < 3; i++)
            {
                double off = anchor.Offset != null && anchor.Offset.Length > i ? anchor.Offset[i] : 0;
                if (off == 0) continue;

                if (el.From != null && el.From.Length > i) el.From[i] += off;
                if (el.To != null && el.To.Length > i) el.To[i] += off;
                if (el.RotationOrigin != null && el.RotationOrigin.Length > i) el.RotationOrigin[i] += off;
            }

            if (anchor.Rotation != null && anchor.Rotation.Length >= 3)
            {
                el.RotationX += anchor.Rotation[0];
                el.RotationY += anchor.Rotation[1];
                el.RotationZ += anchor.Rotation[2];
            }

            double scale = anchor.Scale <= 0 ? 1.0 : anchor.Scale;
            if (scale != 1.0) ScaleChildren(el, scale);
        }


        private static void ScaleChildren(ShapeElement el, double scale)
        {
            if (el.Children == null) return;

            for (int i = 0; i < el.Children.Length; i++)
            {
                ShapeElement child = el.Children[i];
                double[] origin = child.RotationOrigin ?? new double[] { 0, 0, 0 };

                for (int a = 0; a < 3; a++)
                {
                    if (child.From != null && child.From.Length > a) child.From[a] = origin[a] + (child.From[a] - origin[a]) * scale;
                    if (child.To != null && child.To.Length > a) child.To[a] = origin[a] + (child.To[a] - origin[a]) * scale;
                }

                ScaleChildren(child, scale);
            }
        }


        private Shape LoadTagShape(AssetLocation location)
        {
            IAsset asset = capi.Assets.TryGet(location);

            if (asset == null)
            {
                capi.Logger.Error("[eartags] Missing shape asset {0}", location);
                return null;
            }

            return asset.ToObject<Shape>();
        }


        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
        {
            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);

            if (mode == EnumInteractMode.Attack
                && byEntity is EntityPlayer
                && (system == null || system.MetalTagProtects)
                && WearsMetalTag())
            {
                handled = EnumHandling.PreventDefault;
                return;
            }

            if (mode != EnumInteractMode.Interact) return;
            if (!byEntity.Controls.Sneak) return;
            if (itemslot?.Itemstack != null) return;
            if (GetTag(EarTagsModSystem.SideLeft) == null && GetTag(EarTagsModSystem.SideRight) == null) return;

            handled = EnumHandling.PreventDefault;

            if (entity.World.Side != EnumAppSide.Server) return;

            string side = GetTag(EarTagsModSystem.SideRight) != null
                ? EarTagsModSystem.SideRight
                : EarTagsModSystem.SideLeft;

            string material = GetTag(side);
            string kind = GetTagKind(side);
            SetTag(side, null, null);

            Item item = entity.World.GetItem(new AssetLocation("eartags:" + kind + "-" + material));
            IPlayer plr = (byEntity as EntityPlayer)?.Player;

            if (item != null)
            {
                ItemStack stack = new ItemStack(item);
                bool given = plr != null && plr.InventoryManager.TryGiveItemstack(stack);

                if (!given) entity.World.SpawnItemEntity(stack, entity.SidedPos.XYZ.Add(0, 0.5, 0));
            }

            entity.World.PlaySoundAt(new AssetLocation("game:sounds/block/leather"), entity, null, true, 16);

            if (system != null && system.TagMessages)
            {
                (plr as IServerPlayer)?.SendMessage(
                    GlobalConstants.GeneralChatGroup,
                    Lang.Get("eartags:" + Terms + "-removed", Lang.Get("eartags:side-" + side), entity.GetName()),
                    EnumChatType.Notification);
            }
        }


        public override void GetInfoText(StringBuilder infotext)
        {
            base.GetInfoText(infotext);

            StatusLine(infotext);
        }


        private void StatusLine(StringBuilder infotext)
        {
            string badges = Badges();
            if (badges.Length == 0) return;

            infotext.AppendLine(badges);
        }


        private string Badges()
        {
            string result = WearsMetalTag() ? Lang.Get("eartags:info-protected") : "";

            string tamed = EarTagsModSystem.ReadTamedState(entity);
            if (tamed != null)
            {
                string badge = Lang.Get("eartags:info-tamed-" + tamed);
                result = result.Length > 0 ? result + Lang.Get("eartags:info-badge-sep") + badge : badge;
            }

            return result;
        }


        public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled)
        {
            if (GetTag(EarTagsModSystem.SideLeft) == null && GetTag(EarTagsModSystem.SideRight) == null)
            {
                return base.GetInteractionHelp(world, es, player, ref handled);
            }

            return new WorldInteraction[]
            {
                new WorldInteraction()
                {
                    ActionLangCode = "eartags:" + Terms + "-entityhelp-remove",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "sneak",
                    RequireFreeHand = true
                }
            };
        }
    }
}
