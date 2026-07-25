using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace EarTags
{
    /// <summary>
    /// Live tuning commands. Placement is a client-side rendering concern, so /eartags runs on the
    /// client and never touches the server. Freezing an animal has to stop its AI, which only the
    /// server can do, so that lives under its own command name - registering /eartags on both sides
    /// would just have the client shadow the server copy.
    ///
    /// Arguments are taken as one OptionalAll string and split by hand rather than declaring a
    /// parser per subcommand, which keeps the surface we depend on to a single parser.
    /// </summary>
    public static class EarTagsCommands
    {
        private const float ReTesselateRange = 32f;

        private static ICoreServerAPI sapi;
        private static long frozenEntityId;
        private static double frozenX, frozenY, frozenZ;

        /// <summary>
        /// Animations to shut off while an animal is frozen, so it holds a still pose instead of
        /// walking on the spot. Listed explicitly rather than enumerating the animator's active set,
        /// because that collection is a Dictionary and a source mod cannot name one. These are the
        /// codes declared on the vanilla hooved animals; unknown codes are simply ignored.
        /// </summary>
        private static readonly string[] stoppableAnims = new string[]
        {
            "walk", "run", "gallop", "stot", "swim", "idle", "eat", "sit", "sleep",
            "attack", "charge", "roar", "hurt", "die"
        };


        // ---- client: reload / nudge / scale / save ---------------------------------------

        public static void RegisterClient(ICoreClientAPI capi, EarTagsModSystem sys)
        {
            capi.ChatCommands
                .Create("eartags")
                .WithDescription("Tune ear tag placement live. Subcommands: show, reload, nudge, scale, save")
                .WithArgs(
                    capi.ChatCommands.Parsers.OptionalWord("sub"),
                    capi.ChatCommands.Parsers.OptionalWord("arg1"),
                    capi.ChatCommands.Parsers.OptionalWord("arg2"))
                .HandleWith(args => Handle(capi, sys, args));
        }


        private static TextCommandResult Handle(ICoreClientAPI capi, EarTagsModSystem sys, TextCommandCallingArgs args)
        {
            // Three separate OptionalWord parsers rather than one OptionalAll: OptionalAll's value
            // never arrived through the indexer, and ArgCount read as 0 even with arguments typed.
            // Each slot is read defensively and independently.
            //
            // Do NOT reach for args.Parsers as a fallback: it is a List<>, and List is just as
            // unusable in a source mod as Dictionary - both live in the System.Collections facade
            // that the in-game compiler does not reference (CS0012), which takes the whole mod down.
            string[] parts = new string[] { Slot(args, 0), Slot(args, 1), Slot(args, 2) };
            string raw = (parts[0] + " " + parts[1] + " " + parts[2]).Trim();
            string sub = parts[0].ToLowerInvariant();

            EarTagSpeciesConfig cfg = LookedAtConfig(capi, sys);

            switch (sub)
            {
                case "reload":
                    {
                        int n = sys.ReloadFromDisk(capi);
                        if (n < 0) return TextCommandResult.Error("Could not reload. Path: " + sys.ConfigDiskPath());

                        int redrawn = ReTesselateNearby(capi);
                        return TextCommandResult.Success(string.Format(
                            "Reloaded {0} species from disk, redrew {1} animal(s).", n, redrawn));
                    }

                case "save":
                    {
                        bool ok = sys.SaveToDisk(capi);
                        return ok
                            ? TextCommandResult.Success("Saved offsets to attachpoints.json (comments preserved).")
                            : TextCommandResult.Error("Could not write to " + sys.ConfigDiskPath());
                    }

                case "show":
                    {
                        if (cfg == null) return TextCommandResult.Error("Look at a taggable animal with a configured species.");
                        return TextCommandResult.Success(Describe(cfg));
                    }

                case "nudge":
                    {
                        if (cfg == null) return TextCommandResult.Error("Look at a taggable animal with a configured species.");
                        if (parts[1] == "" || parts[2] == "") return TextCommandResult.Error("Usage: .eartags nudge x|y|z amount");

                        int axis = AxisIndex(parts[1]);
                        if (axis < 0) return TextCommandResult.Error("Axis must be x, y or z.");

                        double delta;
                        if (!double.TryParse(parts[2], System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out delta))
                        {
                            return TextCommandResult.Error("Amount must be a number, e.g. 0.1 or -0.05");
                        }

                        // On most ears Z is mirrored between the two sides and X and Y are not.
                        // Applying the sign flip here means the operator never has to think about
                        // it - except on the species that set mirrorZ false, where Z centres the
                        // tag in the thickness of the ear and both sides want the same nudge.
                        bool mirror = axis == 2 && cfg.MirrorZ;

                        Apply(cfg.Left, axis, delta);
                        Apply(cfg.Right, axis, mirror ? -delta : delta);

                        ReTesselateNearby(capi);
                        return TextCommandResult.Success(Describe(cfg));
                    }

                case "scale":
                    {
                        if (cfg == null) return TextCommandResult.Error("Look at a taggable animal with a configured species.");
                        if (parts[1] == "") return TextCommandResult.Error("Usage: .eartags scale value");

                        double s;
                        if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out s) || s <= 0)
                        {
                            return TextCommandResult.Error("Scale must be a positive number, e.g. 1.25");
                        }

                        if (cfg.Left != null) cfg.Left.Scale = s;
                        if (cfg.Right != null) cfg.Right.Scale = s;

                        ReTesselateNearby(capi);
                        return TextCommandResult.Success(Describe(cfg));
                    }

                default:
                    // Single line on purpose - multi-line results render as a blank line in chat.
                    // Echoes the parsed argument so a wrong subcommand is obvious immediately.
                    return TextCommandResult.Success(
                        "eartags [got: " + (raw == null ? "(none)" : raw) + "] | "
                        + "subcommands: show | nudge x|y|z n | scale n | save | reload");
            }
        }


        /// <summary>
        /// Reads one argument slot, returning "" for anything missing. Never throws: the indexer
        /// has already proven it can hand back nulls and unexpected types for absent optional args.
        /// </summary>
        private static string Slot(TextCommandCallingArgs args, int index)
        {
            try
            {
                object o = args[index];
                return o == null ? "" : o.ToString().Trim();
            }
            catch (Exception)
            {
                return "";
            }
        }


        private static EarTagSpeciesConfig LookedAtConfig(ICoreClientAPI capi, EarTagsModSystem sys)
        {
            Entity e = capi.World?.Player?.CurrentEntitySelection?.Entity;
            if (e == null) return null;

            return sys.ResolveAnchors(e.Code?.Path);
        }


        private static void Apply(EarTagAnchor a, int axis, double delta)
        {
            if (a?.Offset == null || a.Offset.Length <= axis) return;
            a.Offset[axis] = Math.Round(a.Offset[axis] + delta, 4);
        }


        private static int AxisIndex(string s)
        {
            switch (s.ToLowerInvariant())
            {
                case "x": return 0;
                case "y": return 1;
                case "z": return 2;
                default: return -1;
            }
        }


        /// <summary>
        /// Both sides on one line. Showing only the left used to be enough - until it wasn't: the
        /// right ear's Z was wrong on every species whose ear is not 1.2 wide, and nothing in the
        /// tuning output would have shown it. Single line on purpose, see the default case above.
        /// </summary>
        private static string Describe(EarTagSpeciesConfig cfg)
        {
            EarTagAnchor l = cfg.Left;
            EarTagAnchor r = cfg.Right;

            return string.Format("{0}  L {1}  R {2}  scale {3}   ({4})",
                cfg.Match,
                Offsets(l), Offsets(r),
                (l ?? r) == null ? "-" : (l ?? r).Scale.ToString("0.###"),
                cfg.MirrorZ ? "z mirrored, L+R should equal earWidth - 1.2" : "z is a centring offset, not mirrored");
        }


        private static string Offsets(EarTagAnchor a)
        {
            if (a?.Offset == null || a.Offset.Length < 3) return "[ none ]";

            return string.Format("[ {0}, {1}, {2} ]",
                a.Offset[0].ToString("0.###"), a.Offset[1].ToString("0.###"), a.Offset[2].ToString("0.###"));
        }


        /// <summary>Forces nearby taggable animals to rebuild their shape so edits show immediately.</summary>
        private static int ReTesselateNearby(ICoreClientAPI capi)
        {
            Vec3d pos = capi.World?.Player?.Entity?.Pos?.XYZ;
            if (pos == null) return 0;

            Entity[] entities = capi.World.GetEntitiesAround(pos, ReTesselateRange, ReTesselateRange,
                e => e.GetBehavior<EntityBehaviorEarTaggable>() != null);

            if (entities == null) return 0;

            for (int i = 0; i < entities.Length; i++) entities[i].MarkShapeModified();

            return entities.Length;
        }


        // ---- server: freeze --------------------------------------------------------------

        public static void RegisterServer(ICoreServerAPI api)
        {
            sapi = api;

            api.ChatCommands
                .Create("eartagfreeze")
                .WithDescription("Pin the animal you are looking at in place, so it stops wandering while you tune tag placement. Run again to release.")
                .RequiresPrivilege("controlserver")
                .RequiresPlayer()
                .HandleWith(OnFreeze);

            api.Event.RegisterGameTickListener(OnServerTick, 20);
        }


        private static TextCommandResult OnFreeze(TextCommandCallingArgs args)
        {
            IServerPlayer plr = args.Caller?.Player as IServerPlayer;
            if (plr == null) return TextCommandResult.Error("Players only.");

            if (frozenEntityId != 0)
            {
                frozenEntityId = 0;
                return TextCommandResult.Success("Released. The animal can move again.");
            }

            Entity e = plr.CurrentEntitySelection?.Entity;
            if (e == null) return TextCommandResult.Error("Look at an animal first.");

            frozenEntityId = e.EntityId;
            frozenX = e.ServerPos.X;
            frozenY = e.ServerPos.Y;
            frozenZ = e.ServerPos.Z;

            return TextCommandResult.Success("Frozen " + e.GetName() + ". Run .eartagfreeze again to release.");
        }


        /// <summary>
        /// Pins the frozen entity by rewriting its position every tick. The AI keeps issuing move
        /// commands - it may still play a walk animation - but it cannot actually go anywhere.
        /// </summary>
        private static void OnServerTick(float dt)
        {
            if (frozenEntityId == 0 || sapi == null) return;

            Entity e = sapi.World.GetEntityById(frozenEntityId);

            if (e == null || !e.Alive)
            {
                frozenEntityId = 0;
                return;
            }

            e.ServerPos.X = frozenX;
            e.ServerPos.Y = frozenY;
            e.ServerPos.Z = frozenZ;
            e.ServerPos.Motion.Set(0, 0, 0);

            e.Pos.X = frozenX;
            e.Pos.Y = frozenY;
            e.Pos.Z = frozenZ;
            e.Pos.Motion.Set(0, 0, 0);

            // The AI keeps re-triggering these every time it decides to walk somewhere, so they
            // have to be stopped continuously rather than once at freeze time.
            if (e.AnimManager != null)
            {
                for (int i = 0; i < stoppableAnims.Length; i++)
                {
                    e.AnimManager.StopAnimation(stoppableAnims[i]);
                }
            }
        }
    }
}
