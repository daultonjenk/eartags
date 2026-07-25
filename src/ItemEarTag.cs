using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace EarTags
{
    public class ItemEarTag : Item
    {
        public string Material { get { return LastCodePart(); } }
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
            string terms = "eartags:" + beh.Terms;

            if (side == null)
            {
                Notify(splr, Lang.Get(terms + (byEntity.Controls.Sneak ? "-rightfull" : "-bothfull"), target.GetName()));
                return;
            }

            if (!beh.CanRenderSide(side))
            {
                Notify(splr, Lang.Get("eartags:unsupported", target.GetName()));
                return;
            }

            if (!HasChiselIfNeeded(byEntity, beh))
            {
                Notify(splr, Lang.Get("eartags:needchisel", target.GetName()));
                return;
            }

            beh.SetTag(side, Kind, Material);

            slot.TakeOut(1);
            slot.MarkDirty();

            byEntity.World.PlaySoundAt(new AssetLocation("game:sounds/block/leather"), target, null, true, 16);

            if (api.ModLoader.GetModSystem<EarTagsModSystem>()?.TagMessages == true)
            {
                Notify(splr, Lang.Get(terms + "-applied",
                    EarTagsModSystem.MaterialName(Kind, Material),
                    Lang.Get("eartags:side-" + side),
                    target.GetName()));
            }
        }


        private static bool HasChiselIfNeeded(EntityAgent byEntity, EntityBehaviorEarTaggable beh)
        {
            EarTagsModSystem sys = byEntity.Api.ModLoader.GetModSystem<EarTagsModSystem>();

            if (sys == null || !sys.RequireChisel) return true;
            if (beh.Terms != EarTagsModSystem.DefaultTerms) return true;

            ItemStack offhand = byEntity.LeftHandItemSlot?.Itemstack;

            return offhand?.Collectible != null
                && offhand.Collectible.Code != null
                && offhand.Collectible.Code.Path.StartsWith("chisel", StringComparison.Ordinal);
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
