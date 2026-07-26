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
        // Semicolon-delimited set of species/texture pairs already warned about. A HashSet would read
        // better, but the game compiles mod code against a fixed reference list that has no
        // System.Collections in it, so no generic collection type is available here - which is also
        // why the shape and entity texture dictionaries below are reached through reflection.
        private static string warnedTextureCodes = ";";
        private static readonly object warnLock = new object();

        private ICoreClientAPI capi;
        private EarTagSpeciesConfig anchors;
        private EarTagsModSystem system;

        /// <summary>The tag set the current mesh was built for, so we can tell a real change from a resync.</summary>
        private string drawnTags;


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
                drawnTags = TagSignature();
                entity.WatchedAttributes.RegisterModifiedListener(EarTagsModSystem.AttrTree, OnTagsChanged);
            }
        }


        private void OnTagsChanged()
        {
            // A full WatchedAttributes resync calls every registered listener regardless of the
            // path it asked for - SyncedTreeAttribute.FromBytes skips the path check that
            // PartialUpdate does - so this fires on animals whose tags did not change, untagged
            // ones included. Rebuilding an animal's mesh is not cheap, so only do it when the
            // tags actually differ from the ones the current mesh was built for.
            string now = TagSignature();
            if (now == drawnTags) return;

            drawnTags = now;
            entity.MarkShapeModified();
        }


        private string TagSignature()
        {
            return GetTagKind(EarTagsModSystem.SideLeft) + ":" + GetTag(EarTagsModSystem.SideLeft)
                + "|" + GetTagKind(EarTagsModSystem.SideRight) + ":" + GetTag(EarTagsModSystem.SideRight);
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


        public override void OnEntityDeath(DamageSource damageSource)
        {
            base.OnEntityDeath(damageSource);

            if (entity.World.Side != EnumAppSide.Server) return;

            foreach (string side in EarTagsModSystem.Sides)
            {
                string material = GetTag(side);
                if (material == null) continue;

                string kind = GetTagKind(side);
                SetTag(side, null, null);

                Item item = entity.World.GetItem(new AssetLocation("eartags:" + kind + "-" + material));
                if (item != null)
                    entity.World.SpawnItemEntity(new ItemStack(item), entity.SidedPos.XYZ.Add(0, 0.5, 0));
            }
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
                    (code, loc) => WarnIfTextureMissing(prefix + code),
                    0f
                );
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[eartags] Failed to attach {0} ear tag to {1}: {2}", side, entity.Code, e);
            }
        }


        /// <summary>
        /// Every supported species gets its tag textures declared by a patches/*-eartag-textures.json
        /// patch, which is what lets the stepparented tag resolve its texture out of the entity's own
        /// collection. This is only the safety net: if a species is added to attachpoints.json without
        /// the matching texture patch, say so once and let the tag render untextured, rather than
        /// writing into the entity type's shared texture collection from inside tesselation.
        /// </summary>
        private void WarnIfTextureMissing(string textureCode)
        {
            object holder = entity.Properties?.Client;
            if (holder == null) return;

            object dict = ReadMember(holder, "Textures");
            if (dict == null) return;

            object present = dict.GetType().GetMethod("ContainsKey")?.Invoke(dict, new object[] { textureCode });
            if (present is bool && (bool)present) return;

            string once = entity.Code.ToShortString() + "/" + textureCode + ";";

            lock (warnLock)
            {
                if (warnedTextureCodes.IndexOf(";" + once, StringComparison.Ordinal) >= 0) return;
                warnedTextureCodes += once;
            }

            capi.Logger.Warning(
                "[eartags] {0} declares no texture '{1}', so its tag will render untextured. "
                + "Add that key to the matching patches/*-eartag-textures.json.",
                entity.Code, textureCode);
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
