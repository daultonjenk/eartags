using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace EarTags
{
    /// <summary>
    /// A small dyed-leather or beaten-metal tag. Right-click an animal to clip it on the first bare
    /// ear, sneak + right-click to put it specifically on the right ear.
    ///
    /// One class serves both items: everything that differs between them is read off the code, so
    /// "eartagmetal-copper" needs no class of its own.
    /// </summary>
    public class ItemEarTag : Item
    {
        /// <summary>
        /// The material variant, e.g. "red" from "eartag-red" or "copper" from
        /// "eartagmetal-copper". Read off the code rather than via Variant, which is a
        /// Dictionary&lt;,&gt; and so unusable in a source mod (see EarTagsModSystem).
        /// </summary>
        public string Material { get { return LastCodePart(); } }

        /// <summary>"eartag" or "eartagmetal" - which of the two items this is.</summary>
        public string Kind { get { return FirstCodePart(); } }


        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            EntityBehaviorEarTaggable beh = entitySel?.Entity?.GetBehavior<EntityBehaviorEarTaggable>();

            if (beh == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            handling = EnumHandHandling.PreventDefault;

            if (byEntity.World.Side != EnumAppSide.Server) return;

            IServerPlayer splr = (byEntity as EntityPlayer)?.Player as IServerPlayer;
            Entity target = entitySel.Entity;

            string side = beh.NextFreeSide(byEntity.Controls.Sneak);

            // Which words this animal's tags get called by is the species' business, not the
            // item's - the same tag is an ear tag on a sheep and a leg band on a chicken.
            string terms = "eartags:" + beh.Terms;

            if (side == null)
            {
                Notify(splr, Lang.Get(terms + (byEntity.Controls.Sneak ? "-rightfull" : "-bothfull"), target.GetName()));
                return;
            }

            // The behavior is on every sheep, but only species listed in attachpoints.json can
            // actually show a tag. Refuse rather than silently swallowing the tag.
            if (!beh.CanRenderSide(side))
            {
                Notify(splr, Lang.Get("eartags:unsupported", target.GetName()));
                return;
            }

            beh.SetTag(side, Kind, Material);

            slot.TakeOut(1);
            slot.MarkDirty();

            byEntity.World.PlaySoundAt(new AssetLocation("game:sounds/block/leather"), target, null, true, 16);

            Notify(splr, Lang.Get(terms + "-applied",
                EarTagsModSystem.MaterialName(Kind, Material),
                Lang.Get("eartags:side-" + side),
                target.GetName()));
        }


        private static void Notify(IServerPlayer splr, string message)
        {
            splr?.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
        }


        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            dsc.AppendLine(Lang.Get("eartags:" + Kind + "-itemdesc"));
        }


        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction()
                {
                    ActionLangCode = "eartags:heldhelp-apply",
                    MouseButton = EnumMouseButton.Right
                },
                new WorldInteraction()
                {
                    ActionLangCode = "eartags:heldhelp-applyright",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "sneak"
                }
            };
        }
    }
}
