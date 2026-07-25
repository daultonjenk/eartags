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
    /// Lets an animal wear up to two coloured tags, one per side. The colour per side lives in the
    /// entity's WatchedAttributes so it syncs to clients and survives save/load, and the tag
    /// geometry is step-parented onto the animal's ear bone at tesselation time - the same
    /// mechanism vanilla uses for the mouflon's mane and the boar's tusks.
    ///
    /// "Ear" is the common case rather than the only one: which bone, which shape and which words
    /// all come from the species' entry in attachpoints.json, so the same behaviour puts a band
    /// round a chicken's leg.
    /// </summary>
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

        /// <summary>Material worn on the given ear - a colour or a metal - or null if it is bare.</summary>
        public string GetTag(string side)
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute(EarTagsModSystem.AttrTree);
            if (tree == null) return null;

            string material = tree.GetString(side);
            return string.IsNullOrEmpty(material) ? null : material;
        }


        /// <summary>
        /// Which item is on that ear, "eartag" or "eartagmetal". Tags saved before metal existed
        /// carry no kind, so an absent value reads as leather rather than as nothing.
        /// </summary>
        public string GetTagKind(string side)
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute(EarTagsModSystem.AttrTree);
            if (tree == null) return EarTagsModSystem.KindLeather;

            string kind = tree.GetString(side + EarTagsModSystem.AttrKindSuffix);
            return string.IsNullOrEmpty(kind) ? EarTagsModSystem.KindLeather : kind;
        }


        /// <summary>Sets or (with a null material) clears the tag on one ear. Server side only.</summary>
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


        /// <summary>Whether any ear carries a metal tag, which is what grants the protection.</summary>
        public bool WearsMetalTag()
        {
            for (int i = 0; i < EarTagsModSystem.Sides.Length; i++)
            {
                string side = EarTagsModSystem.Sides[i];
                if (GetTag(side) != null && GetTagKind(side) == EarTagsModSystem.KindMetal) return true;
            }

            return false;
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


        /// <summary>
        /// Lang key prefix for anything this animal's tags need to say. Species without an entry
        /// still have to produce a message - they get refused by the item - so this falls back
        /// rather than returning null.
        /// </summary>
        public string Terms
        {
            get { return anchors == null ? EarTagsModSystem.DefaultTerms : anchors.TermsOrDefault; }
        }


        // ---- protection ------------------------------------------------------------------

        /// <summary>
        /// A metal tag makes its wearer immune to anything the player does to it. The point is the
        /// accidents - the cleaver swung one animal too far at harvest time, the punch that lands
        /// on a sheep instead of the block behind it - so a prize breeder can be marked as
        /// off limits and stay that way.
        ///
        /// Only the player is blocked. Wolves, falls and drowning still work, so a tagged animal
        /// is protected rather than immortal, and to slaughter one you take the tag off first.
        ///
        /// ORDER MATTERS. Entity.ReceiveDamage hands the damage to each behaviour by ref in list
        /// order, and EntityBehaviorHealth is the one that subtracts it, so zeroing the damage only
        /// works if this behaviour comes first. That is why the patches insert the server copy at
        /// /server/behaviors/0 rather than appending it.
        /// </summary>
        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            base.OnEntityReceiveDamage(damageSource, ref damage);

            if (damageSource == null || damage <= 0) return;
            if (damageSource.Type == EnumDamageType.Heal) return;
            if (!IsPlayerCaused(damageSource)) return;
            if (!WearsMetalTag()) return;

            damage = 0;
        }


        /// <summary>
        /// Whether the player is behind this damage. Source covers a direct hit; the two entity
        /// fields catch anything the player set going, an arrow being the obvious one - CauseEntity
        /// is the archer where SourceEntity is the arrow.
        /// </summary>
        private static bool IsPlayerCaused(DamageSource damageSource)
        {
            if (damageSource.Source == EnumDamageSource.Player) return true;

            return damageSource.SourceEntity is EntityPlayer || damageSource.CauseEntity is EntityPlayer;
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

                // The tag borrows vanilla's own dyed leather and ingot textures, so neither the
                // colours nor the twenty-three metals cost us any art.
                SetShapeTexture(tagShape, "tag", EarTagsModSystem.MaterialTexture(kind, material));

                // texturePrefixCode keeps our texture key from colliding with the animal's own
                // ("hide"). Keying it by kind and material rather than by side means the two ears
                // share one atlas entry when they match, and re-tagging an ear lands on a different
                // key instead of silently reusing the old material.
                string prefix = kind + material;

                entityShape.StepParentShape(
                    tagShape,
                    prefix,
                    shapeLocation.ToShortString(),
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

            string material = GetTag(side);
            string kind = GetTagKind(side);
            SetTag(side, null, null);

            Item item = entity.World.GetItem(new AssetLocation("eartags:" + kind + "-" + material));
            IPlayer plr = (byEntity as EntityPlayer)?.Player;

            if (item != null)
            {
                // Hand it back rather than destroying it - retagging shouldn't cost leather.
                ItemStack stack = new ItemStack(item);
                bool given = plr != null && plr.InventoryManager.TryGiveItemstack(stack);

                if (!given) entity.World.SpawnItemEntity(stack, entity.SidedPos.XYZ.Add(0, 0.5, 0));
            }

            entity.World.PlaySoundAt(new AssetLocation("game:sounds/block/leather"), entity, null, true, 16);

            // Off unless someone asked for it - see EarTagsConfig. The sound is the real feedback.
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

            if (system == null || system.VisualInfo) StatusLine(infotext);
            else WordyLines(infotext);
        }


        /// <summary>
        /// The shared status line, directly under the animal's name: this mod's swatches on the
        /// left, everyone's badges on the right, then a blank line to hold it apart from the
        /// numbers below. Sitting first is what makes it read as a subtitle of the name rather
        /// than as an interruption halfway down a list - see the client behaviour patch, which
        /// puts this behaviour at index 0 for exactly that reason.
        ///
        /// Renders whenever there is anything at all to say, so a tamed but untagged animal still
        /// gets its badge, and an animal with neither takes up no room.
        ///
        /// No blank line after it. The panel already puts a gap between the title and the first
        /// body line - which a mod cannot change, it is the panel's own padding - and adding one
        /// below as well left the row floating in the middle of two gaps instead of sitting under
        /// the name. One gap above, none below, and it reads as a subtitle.
        /// </summary>
        private void StatusLine(StringBuilder infotext)
        {
            bool tagged = GetTag(EarTagsModSystem.SideLeft) != null || GetTag(EarTagsModSystem.SideRight) != null;

            string badges = Badges();
            if (!tagged && badges.Length == 0) return;

            StringBuilder line = new StringBuilder();

            if (tagged)
            {
                line.Append(Swatch(EarTagsModSystem.SideLeft));
                line.Append(' ');
                line.Append(Swatch(EarTagsModSystem.SideRight));
            }

            if (tagged && badges.Length > 0) line.Append(Lang.Get("eartags:info-gap"));
            line.Append(badges);

            infotext.AppendLine(line.ToString());
        }


        /// <summary>
        /// The badge cluster. Protection is ours; the tame state is whatever another mod published
        /// into the shared tree. Both are whole VTML fragments out of the lang file, so a mod that
        /// wants a different icon changes en.json rather than code.
        /// </summary>
        private string Badges()
        {
            StringBuilder badges = new StringBuilder();

            if (WearsMetalTag()) badges.Append(Lang.Get("eartags:info-protected"));

            string tamed = EarTagsModSystem.ReadTamedState(entity);

            if (tamed != null)
            {
                if (badges.Length > 0) badges.Append(Lang.Get("eartags:info-badge-sep"));
                badges.Append(Lang.Get("eartags:info-tamed-" + tamed));
            }

            return badges.ToString();
        }


        /// <summary>
        /// The original wording, for anyone who turns the swatches off or whose font has no square
        /// in it. It has to carry the published tame state too, or switching to this mode would
        /// silently drop something a sibling mod is relying on us to show.
        /// </summary>
        private void WordyLines(StringBuilder infotext)
        {
            if (GetTag(EarTagsModSystem.SideLeft) != null || GetTag(EarTagsModSystem.SideRight) != null)
            {
                infotext.AppendLine(Lang.Get("eartags:" + Terms + "-worn", WordySummary()));

                // An animal that quietly refuses to die reads as a bug rather than as a decision
                // someone made about it on purpose.
                if (WearsMetalTag()) infotext.AppendLine(Lang.Get("eartags:protected"));
            }

            string tamed = EarTagsModSystem.ReadTamedState(entity);
            if (tamed != null) infotext.AppendLine(Lang.Get("eartags:status-tamed-" + tamed));
        }


        /// <summary>
        /// "left red, right blue" - the original wording, kept for anyone who turns the swatches
        /// off or whose font has no square in it.
        /// </summary>
        private string WordySummary()
        {
            StringBuilder worn = new StringBuilder();

            for (int i = 0; i < EarTagsModSystem.Sides.Length; i++)
            {
                string side = EarTagsModSystem.Sides[i];
                string material = GetTag(side);
                if (material == null) continue;

                if (worn.Length > 0) worn.Append(", ");

                worn.Append(Lang.Get("eartags:worn-entry",
                    Lang.Get("eartags:side-" + side),
                    EarTagsModSystem.MaterialName(GetTagKind(side), material)));
            }

            return worn.ToString();
        }


        /// <summary>
        /// One side's square, wrapped in the VTML the info panel colours it by. Bare sides get the
        /// hollow glyph and no colour, so they read as absence rather than as a grey tag - which
        /// matters, because which side a tag is on is half the information.
        /// </summary>
        private string Swatch(string side)
        {
            string material = GetTag(side);

            if (material == null) return Lang.Get("eartags:info-swatch-bare");

            return "<font color=\"" + EarTagsModSystem.MaterialHex(material) + "\">"
                + Lang.Get("eartags:info-swatch") + "</font>";
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
