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
    /// <summary>
    /// Lets an animal wear up to two coloured ear tags. The tag colour per ear lives in the
    /// entity's WatchedAttributes so it syncs to clients and survives save/load, and the tag
    /// geometry is step-parented onto the animal's ear bone at tesselation time - the same
    /// mechanism vanilla uses for the mouflon's mane and the boar's tusks.
    /// </summary>
    public class EntityBehaviorEarTaggable : EntityBehavior
    {
        private static readonly AssetLocation tagShapeLocation = new AssetLocation("eartags:shapes/entity/eartag.json");

        private ICoreClientAPI capi;
        private EarTagSpeciesConfig anchors;


        public EntityBehaviorEarTaggable(Entity entity) : base(entity) { }

        public override string PropertyName() { return "eartaggable"; }


        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            EarTagsModSystem sys = entity.Api.ModLoader.GetModSystem<EarTagsModSystem>();
            anchors = sys?.ResolveAnchors(entity.Code?.Path);

            capi = entity.Api as ICoreClientAPI;

            if (capi != null)
            {
                // Re-tesselate as soon as a tag is added or removed, otherwise the change would
                // only appear the next time the entity's shape happened to be rebuilt.
                entity.WatchedAttributes.RegisterModifiedListener(EarTagsModSystem.AttrTree, OnTagsChanged);
            }
        }


        private void OnTagsChanged()
        {
            entity.MarkShapeModified();
        }


        // ---- tag state -------------------------------------------------------------------

        /// <summary>Colour worn on the given ear, or null if that ear is bare.</summary>
        public string GetTag(string side)
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute(EarTagsModSystem.AttrTree);
            if (tree == null) return null;

            string color = tree.GetString(side);
            return string.IsNullOrEmpty(color) ? null : color;
        }


        /// <summary>Sets or (with a null colour) clears the tag on one ear. Server side only.</summary>
        public void SetTag(string side, string color)
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetOrAddTreeAttribute(EarTagsModSystem.AttrTree);

            if (color == null) tree.RemoveAttribute(side);
            else tree.SetString(side, color);

            entity.WatchedAttributes.MarkPathDirty(EarTagsModSystem.AttrTree);
        }


        /// <summary>The ear a newly applied tag should go on, or null if both ears are taken.</summary>
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


        // ---- rendering -------------------------------------------------------------------

        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
        {
            base.OnTesselation(ref entityShape, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);

            if (capi == null || anchors == null) return;

            string left = GetTag(EarTagsModSystem.SideLeft);
            string right = GetTag(EarTagsModSystem.SideRight);
            if (left == null && right == null) return;

            // Never modify the shared per-entitytype shape in place.
            if (!shapeIsCloned)
            {
                entityShape = entityShape.Clone();
                shapeIsCloned = true;
            }

            if (left != null) AttachTag(entityShape, EarTagsModSystem.SideLeft, left, shapePathForLogging);
            if (right != null) AttachTag(entityShape, EarTagsModSystem.SideRight, right, shapePathForLogging);
        }


        private void AttachTag(Shape entityShape, string side, string color, string shapePathForLogging)
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

                Shape tagShape = LoadTagShape();
                if (tagShape?.Elements == null || tagShape.Elements.Length == 0) return;

                ShapeElement el = tagShape.Elements[0];
                el.Name = "EarTag-" + side;
                el.StepParentName = anchor.Bone;
                ApplyAnchor(el, anchor);

                // The tag borrows vanilla's dyed leather textures, so the colour costs us no art.
                SetShapeTexture(tagShape, "tag", new AssetLocation("game", "block/leather/" + color));

                // texturePrefixCode keeps our texture key from colliding with the animal's own
                // ("hide"). Keying it by colour rather than by side means the two ears share one
                // atlas entry when they match, and re-tagging an ear a different colour lands on
                // a different key instead of silently reusing the old colour.
                string prefix = "eartag" + color;

                entityShape.StepParentShape(
                    tagShape,
                    prefix,
                    tagShapeLocation.ToShortString(),
                    shapePathForLogging,
                    capi.Logger,
                    // The callback reports the ORIGINAL texture code ("tag"), but SubclassForStepParenting
                    // has already rewritten the shape's faces to reference prefix + code. Register
                    // under the prefixed name or the tesselator will not find the mapping.
                    (code, loc) => RegisterEntityTexture(prefix + code, loc),
                    0f
                );
            }
            catch (Exception e)
            {
                // A broken tag must never take the whole animal's rendering down with it.
                capi.Logger.Warning("[eartags] Failed to attach {0} ear tag to {1}: {2}", side, entity.Code, e);
            }
        }


        /// <summary>
        /// Adds a texture to the entity's client texture set so the tesselator can find it.
        ///
        /// The texture MUST be inserted into the entity atlas and given a Baked record here. The
        /// tesselator's TextureSource constructor walks this collection and dereferences
        /// Baked.TextureSubId on every entry, so handing it a plain unbaked CompositeTexture
        /// throws a NullReferenceException and takes down the render loop.
        ///
        /// Goes through reflection because this collection and Shape.Textures are both
        /// Dictionary&lt;,&gt;, which a source mod cannot name directly (see EarTagsModSystem).
        /// </summary>
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

                // A CompositeTexture holds the short form ("block/leather/red"), but the atlas
                // loads a real file and needs the full asset path. If that does not resolve, fall
                // back to the location as given rather than silently atlasing the "?" placeholder.
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

                // GetOrInsertTexture returns true even when it quietly atlases the unknown-texture
                // placeholder, so compare the position we got back against the atlas's own.
                object unknownPos = ReadMember(capi.EntityTextureAtlas, "UnknownTexturePosition");

                capi.Logger.Notification(
                    "[eartags] DIAG {0}: assetFound={1} isUnknownTexPos={2} atlasPage={3} rect={4},{5}-{6},{7}",
                    texPath, resolved, ReferenceEquals(texPos, unknownPos),
                    ReadMember(texPos, "atlasTextureId"),
                    ReadMember(texPos, "x1"), ReadMember(texPos, "y1"),
                    ReadMember(texPos, "x2"), ReadMember(texPos, "y2"));

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

                object stored = dt.GetMethod("ContainsKey")?.Invoke(dict, new object[] { textureCode });

                capi.Logger.Notification("[eartags] Registered texture '{0}' -> {1} (subId {2}, stored {3})",
                    textureCode, texPath, subId, stored == null ? "unverified" : stored.ToString());
            }
            catch (Exception e)
            {
                // Leaving the entry out renders the tag untextured, which beats crashing.
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


        /// <summary>
        /// Applies the configured offset, rotation and scale. <paramref name="el"/> is the
        /// invisible origin element that mirrors the ear bone, so translating and rotating it
        /// carries the whole tag with it. Scale has to go to the children, since the origin
        /// element's own box is just a frame of reference.
        /// </summary>
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


        /// <summary>Scales child geometry about each element's own rotation origin.</summary>
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


        /// <summary>
        /// Re-parses the tag shape on every call, deliberately. Shape.Clone() does not deep-copy
        /// the elements' face texture references, so caching a template and cloning it lets
        /// SubclassForStepParenting rewrite the shared original - the texture prefix then
        /// accumulates on every tesselation ("eartagredeartagredeartagred...tag") and the mapping
        /// breaks. Parsing a two-element shape is cheap and tesselation is infrequent.
        /// </summary>
        private Shape LoadTagShape()
        {
            IAsset asset = capi.Assets.TryGet(tagShapeLocation);

            if (asset == null)
            {
                capi.Logger.Error("[eartags] Missing shape asset {0}", tagShapeLocation);
                return null;
            }

            return asset.ToObject<Shape>();
        }


        // ---- interaction -----------------------------------------------------------------

        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
        {
            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);

            if (mode != EnumInteractMode.Interact) return;
            if (!byEntity.Controls.Sneak) return;
            if (itemslot?.Itemstack != null) return;      // bare hand only
            if (GetTag(EarTagsModSystem.SideLeft) == null && GetTag(EarTagsModSystem.SideRight) == null) return;

            handled = EnumHandling.PreventDefault;

            if (entity.World.Side != EnumAppSide.Server) return;

            // Take the most recently added tag off first: right ear, then left.
            string side = GetTag(EarTagsModSystem.SideRight) != null
                ? EarTagsModSystem.SideRight
                : EarTagsModSystem.SideLeft;

            string color = GetTag(side);
            SetTag(side, null);

            Item item = entity.World.GetItem(new AssetLocation("eartags:eartag-" + color));
            IPlayer plr = (byEntity as EntityPlayer)?.Player;

            if (item != null)
            {
                // Hand it back rather than destroying it - retagging shouldn't cost leather.
                ItemStack stack = new ItemStack(item);
                bool given = plr != null && plr.InventoryManager.TryGiveItemstack(stack);

                if (!given) entity.World.SpawnItemEntity(stack, entity.SidedPos.XYZ.Add(0, 0.5, 0));
            }

            entity.World.PlaySoundAt(new AssetLocation("game:sounds/block/leather"), entity, null, true, 16);

            (plr as IServerPlayer)?.SendMessage(
                GlobalConstants.GeneralChatGroup,
                Lang.Get("eartags:removed", Lang.Get("eartags:side-" + side), entity.GetName()),
                EnumChatType.Notification);
        }


        public override void GetInfoText(StringBuilder infotext)
        {
            base.GetInfoText(infotext);

            string left = GetTag(EarTagsModSystem.SideLeft);
            string right = GetTag(EarTagsModSystem.SideRight);
            if (left == null && right == null) return;

            StringBuilder worn = new StringBuilder();

            for (int i = 0; i < EarTagsModSystem.Sides.Length; i++)
            {
                string side = EarTagsModSystem.Sides[i];
                string color = GetTag(side);
                if (color == null) continue;

                if (worn.Length > 0) worn.Append(", ");

                worn.Append(Lang.Get("eartags:worn-entry",
                    Lang.Get("eartags:side-" + side),
                    Lang.Get("eartags:color-" + color)));
            }

            infotext.AppendLine(Lang.Get("eartags:worn", worn.ToString()));
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
                    ActionLangCode = "eartags:entityhelp-remove",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "sneak",
                    RequireFreeHand = true
                }
            };
        }
    }
}
