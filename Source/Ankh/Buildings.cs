using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Ankh
{
    public class Buildings
    {
        public class Building_ZAP : Building
        {
            public override IEnumerable<Gizmo> GetGizmos()
            {
                foreach (Gizmo g in base.GetGizmos())
                    yield return g;

                if (Behaviour_Interpreter.zapCount > 0)
                {
                    Command_Action zap = new Command_Action()
                    {
                        defaultLabel = "Strike down with zap's wrath",
                        defaultDesc = "When the mighty god of lightning favors you, he will give you control over the fire of the skies",
                        icon = ContentFinder<Texture2D>.Get("UI/Overlays/NeedsPower", true),
                        activateSound = SoundDefOf.Click
                    };
                    zap.action = delegate
                    {
                        Find.Targeter.BeginTargeting(TargetingParameters.ForAttackAny(), lti =>
                        {
                            this.Map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(Find.VisibleMap, UI.MouseCell()));
                            if (Behaviour_Interpreter._instance.SubtractZap() > 0) Behaviour_Interpreter._instance.WaitAndExecute(() => zap.action());
                        }, null, null, zap.icon);
                    };
                    yield return zap;
                }
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(base.GetInspectString());
                sb.AppendLine("Zap favors: " + Behaviour_Interpreter.zapCount);
                return sb.ToString();
            }
        }
        public class Building_Therm : Building
        {
            public override IEnumerable<Gizmo> GetGizmos()
            {
                foreach (Gizmo g in base.GetGizmos())
                    yield return g;

                if (Behaviour_Interpreter.thermCount > 0)
                {
                    Command_Action therm = new Command_Action()
                    {
                        defaultLabel = "Therm's Revelation",
                        defaultDesc = "Channel Therm's favor deep into the earth to uncover a steam geyser.",
                        icon = ContentFinder<Texture2D>.Get("Things/Building/Natural/SteamGeyser", true),
                        activateSound = SoundDefOf.Click
                    };
                    therm.action = delegate
                    {
                        Find.Targeter.BeginTargeting(new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(Find.VisibleMap) || !c.IsValid || !c.Cell.InBounds(Find.VisibleMap))
                                {
                                    return false;
                                }
                                List<IntVec3> cells = GenAdj.CellsOccupiedBy(c.Cell, Rot4.North, ThingDefOf.GeothermalGenerator.Size).ToList();

                                foreach (IntVec3 current in cells)
                                {
                                    if(c.Cell.InBounds(Find.VisibleMap))
                                    if (!current.Standable(Find.VisibleMap) || current.GetFirstThing(Find.VisibleMap, ThingDefOf.SteamGeyser) != null || !current.GetTerrain(Find.VisibleMap).affordances.Contains(TerrainAffordance.Heavy))
                                        return false;
                                }
                                bool returns = Find.VisibleMap.reachability.CanReachColony(c.Cell);
                                if (returns)
                                    GenDraw.DrawFieldEdges(cells);
                                return returns;
                            },
                            canTargetLocations = true
                        }, lti =>
                        {
                            Thing geyser = GenSpawn.Spawn(ThingDefOf.SteamGeyser, lti.Cell, Find.VisibleMap);

                            foreach (IntVec3 current in GenAdj.CellsOccupiedBy(geyser))
                            {
                                if (current.IsValid && current.InBounds(Find.VisibleMap))
                                {
                                    foreach (Thing current2 in Find.VisibleMap.thingGrid.ThingsAt(current))
                                    {
                                        if (current2 is Plant || current2 is Filth)
                                        {
                                            current2.Destroy(DestroyMode.Vanish);
                                        }
                                    }
                                }
                            }

                            if (Behaviour_Interpreter._instance.SubtractTherm() > 0) Behaviour_Interpreter._instance.WaitAndExecute(() => therm.action());
                        }, null, null, therm.icon);
                    };
                    yield return therm;

                    Command_Action extinguish = new Command_Action()
                    {
                        defaultLabel = "Therm's  Respite",
                        defaultDesc = "Channel Therm's favor into an inferno to calm the flames.",
                        icon = ContentFinder<Texture2D>.Get("UI_Therm", true),
                        activateSound = SoundDefOf.Click
                    };
                    extinguish.action = delegate
                    {
                        Find.Targeter.BeginTargeting(new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(Find.VisibleMap) || !c.IsValid || !c.Cell.InBounds(Find.VisibleMap))
                                {
                                    return false;
                                }
                                List<IntVec3> cells = GenAdj.CellsOccupiedBy(c.Cell, Rot4.North, new IntVec2(5,5)).ToList();

                                bool returns = false;
                                foreach (IntVec3 current in cells)
                                {
                                    if (c.Cell.InBounds(Find.VisibleMap))
                                        if (current.GetFirstThing(Find.VisibleMap, ThingDefOf.Fire) != null)
                                        returns = true;
                                }
                                if (returns)
                                    GenDraw.DrawFieldEdges(cells);
                                return returns;
                            },
                            canTargetLocations = true
                        }, c =>
                        {
                            foreach (IntVec3 current in GenAdj.CellsOccupiedBy(c.Cell, Rot4.North, new IntVec2(5,5)))
                            {
                                if (current.IsValid && current.InBounds(Find.VisibleMap))
                                {
                                    Thing[] list = current.GetThingList(Find.VisibleMap).Where(t => t.def == ThingDefOf.Fire).ToArray();
                                    foreach(Thing current2 in list)
                                    {
                                        current2.Destroy();
                                        MoteMaker.ThrowMetaPuff(current.ToVector3(), Find.VisibleMap);
                                    }
                                }
                            }

                            if (Behaviour_Interpreter._instance.SubtractTherm() > 0) Behaviour_Interpreter._instance.WaitAndExecute(() => extinguish.action());  //LongEventHandler.QueueLongEvent(() => extinguish.action(), "", false, null);
                        }, null, null, extinguish.icon);
                    };

                    yield return extinguish;
                }
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(base.GetInspectString());
                sb.AppendLine("Therm favors: " + Behaviour_Interpreter.thermCount);
                return sb.ToString();
            }
        }
    }
}