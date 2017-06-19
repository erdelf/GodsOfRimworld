using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Ankh
{
    public class Buildings
    {
        public class Building_ZAP : Building, IAttackTargetSearcher
        {
            public Thing Thing => this;

            public Verb CurrentEffectiveVerb => throw new NotImplementedException();

            public LocalTargetInfo LastAttackedTarget => throw new NotImplementedException();

            public int LastAttackTargetTick => throw new NotImplementedException();

            public override void TickRare()
            {
                List<IAttackTarget> potentialTargetsFor = this.Map.attackTargetsCache.GetPotentialTargetsFor(this);
                LocalTargetInfo target = GenClosest.ClosestThing_Global(this.Position, potentialTargetsFor, 25.9f, t => !((t as Pawn)?.Downed ?? false));

                if (target.IsValid && BehaviourInterpreter.staticVariables.zapCount > 0)
                {
                    this.Map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(this.Map, target.Cell));
                    BehaviourInterpreter.staticVariables.zapCount--;
                }
            }

            public override void DrawExtraSelectionOverlays()
            {
                GenDraw.DrawRadiusRing(this.DrawPos.ToIntVec3(), 25.9f);
                base.DrawExtraSelectionOverlays();
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(base.GetInspectString());
                sb.AppendLine("Zap favors: " + BehaviourInterpreter.staticVariables.zapCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_THERM : Building
        {
            public override IEnumerable<Gizmo> GetGizmos()
            {
                foreach (Gizmo g in base.GetGizmos())
                    yield return g;

                if (BehaviourInterpreter.staticVariables.thermCount > 0)
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
                                    if(current.InBounds(Find.VisibleMap))
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
                            BehaviourInterpreter.staticVariables.thermCount--;
                            if (BehaviourInterpreter.staticVariables.thermCount > 0) BehaviourInterpreter._instance.WaitAndExecute(() => therm.action());
                        }, null, null, therm.icon);
                    };
                    yield return therm;

                    Command_Action extinguish = new Command_Action()
                    {
                        defaultLabel = "Therm's  Respite",
                        defaultDesc = "Channel Therm's favor into an inferno to calm the flames.",
                        icon = ContentFinder<Texture2D>.Get("UI_therm", true),
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
                                List<IntVec3> cells = GenAdj.CellsOccupiedBy(c.Cell, Rot4.North, new IntVec2(6,6)).ToList();

                                bool returns = false;
                                foreach (IntVec3 current in cells)
                                {
                                    if (current.InBounds(Find.VisibleMap))
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
                            foreach (IntVec3 current in GenAdj.CellsOccupiedBy(c.Cell, Rot4.North, new IntVec2(6,6)))
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
                            BehaviourInterpreter.staticVariables.thermCount--;
                            if (BehaviourInterpreter.staticVariables.thermCount > 0) BehaviourInterpreter._instance.WaitAndExecute(() => extinguish.action());  //LongEventHandler.QueueLongEvent(() => extinguish.action(), "", false, null);
                        }, null, null, extinguish.icon);
                    };

                    yield return extinguish;
                }
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(base.GetInspectString());
                sb.AppendLine("Therm favors: " + BehaviourInterpreter.staticVariables.thermCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_PEG : Building
        {
            public override IEnumerable<Gizmo> GetGizmos()
            {
                foreach (Gizmo g in base.GetGizmos())
                    yield return g;

                if (BehaviourInterpreter.staticVariables.pegCount > 0)
                {
                    Command_Action bomb = new Command_Action()
                    {
                        defaultLabel = "peg's wrath",
                        defaultDesc = "Peg favors you",
                        icon = ContentFinder<Texture2D>.Get("UI_peg", true),
                        activateSound = SoundDefOf.Click
                    };
                    bomb.action = delegate
                    {
                        Find.Targeter.BeginTargeting(new TargetingParameters()
                        {
                            validator = lti =>
                            {
                                if (lti.Cell.Fogged(Find.VisibleMap) || !lti.IsValid || !lti.Cell.InBounds(Find.VisibleMap) || Mathf.Sqrt(lti.Cell.DistanceToSquared(this.Position)) > 26f)
                                {
                                    return false;
                                }
                                int cells = GenRadial.NumCellsInRadius(3.9f);

                                bool returns = false;
                                for (int i = 0; i < cells; i++)
                                {
                                    IntVec3 current = lti.Cell + GenRadial.RadialPattern[i];
                                    if (current.InBounds(Find.VisibleMap))
                                        if (!current.GetThingList(Find.VisibleMap).NullOrEmpty())
                                            returns = true;
                                }
                                if (returns)
                                    GenDraw.DrawRadiusRing(lti.Cell, 3.9f);
                                return returns;
                            },
                            canTargetPawns = true,
                            canTargetLocations = true
                        }, lti =>
                        {
                            GenExplosion.DoExplosion(lti.Cell, this.Map, 3.9f, DamageDefOf.Bomb, null);

                            BehaviourInterpreter.staticVariables.pegCount--;
                            if (BehaviourInterpreter.staticVariables.pegCount > 0)
                                BehaviourInterpreter._instance.WaitAndExecute(() => bomb.action());
                        }, null, null, bomb.icon);
                    };
                    yield return bomb;
                }
            }

            public override void DrawExtraSelectionOverlays()
            {
                GenDraw.DrawRadiusRing(this.DrawPos.ToIntVec3(), 25.9f);
                base.DrawExtraSelectionOverlays();
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(base.GetInspectString());
                sb.AppendLine("Peg favors: " + BehaviourInterpreter.staticVariables.pegCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_REPO : Building
        {
            static FieldInfo resolvedDesignatorInfo = typeof(DesignationCategoryDef).GetField("resolvedDesignators", BindingFlags.Instance | BindingFlags.NonPublic);

            public override void SpawnSetup(Map map, bool respawningAfterLoad)
            {
                base.SpawnSetup(map, respawningAfterLoad);
                resolvedDesignatorInfo.SetValue(this.def.designationCategory, (resolvedDesignatorInfo.GetValue(this.def.designationCategory) as List<Designator>).Where(d => !(d is Designator_Build build) || !build.PlacingDef.Equals(this.def)).ToList());
            }

            public override IEnumerable<Gizmo> GetGizmos()
            {
                foreach (Gizmo g in base.GetGizmos())
                    yield return g;

                if (BehaviourInterpreter.staticVariables.repoCount > 0)
                {
                    Command_Action restore = new Command_Action()
                    {
                        defaultLabel = "Repo's Restoration",
                        defaultDesc = "Channel Repo's favor into a colonists body.",
                        icon = ContentFinder<Texture2D>.Get("Things/Mote/FeedbackEquip", true),
                        activateSound = SoundDefOf.Click
                    };
                    restore.action = delegate
                    {
                        Find.Targeter.BeginTargeting(new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(Find.VisibleMap) || !c.IsValid || !c.Cell.InBounds(Find.VisibleMap))
                                {
                                    return false;
                                }
                                return (c.Cell.GetFirstPawn(c.Map)?.IsColonistPlayerControlled ?? false) && Mathf.Sqrt(c.Cell.DistanceToSquared(this.Position)) < 26f;
                            },
                            canTargetLocations = false,
                            canTargetPawns = true
                        }, lti =>
                        {
                            Pawn p = lti.Thing as Pawn;

                            MethodInfo missing = typeof(HealthCardUtility).GetMethod("VisibleHediffs", BindingFlags.Static | BindingFlags.NonPublic);
                            BodyPartRecord record = null;

                            if (((IEnumerable<Hediff>)missing.Invoke(null, new object[] { p, false })).Where(
                                h => h.GetType() == typeof(Hediff_MissingPart)).TryRandomElement(out Hediff result))
                            {
                                record = result.Part;
                                p.health.RestorePart(record);
                            }
                            else if (p.health.hediffSet.hediffs.Where(h => h is Hediff_Injury || h.IsOld()).TryRandomElement(out result))
                            {
                                record = result.Part;
                                result.Heal(result.Severity + 1);
                            }
                            else
                            {
                                while(record == null)
                                    record = p.health.hediffSet.GetRandomNotMissingPart(DamageDefOf.Bullet);
                            }

                            DefDatabase<RecipeDef>.AllDefsListForReading.Where(rd => (rd.addsHediff?.addedPartProps?.isBionic ?? false) &&
                                (rd.appliedOnFixedBodyParts?.Select(bdp => bdp.defName).Contains(record.def.defName) ?? false)).TryRandomElement(out RecipeDef recipe);

                            if (recipe == null)
                                return;

                            recipe.Worker.ApplyOnPawn(p, record, null, recipe.fixedIngredientFilter.AllowedThingDefs.Select(td => ThingMaker.MakeThing(td, td.MadeFromStuff ? GenStuff.DefaultStuffFor(td) : null)).ToList());

                            BehaviourInterpreter.staticVariables.repoCount--;
                            if (BehaviourInterpreter.staticVariables.repoCount > 0)
                                BehaviourInterpreter._instance.WaitAndExecute(() => restore.action());
                        }, null, null, restore.icon);
                    };
                    yield return restore;
                }
            }

            public override void DrawExtraSelectionOverlays()
            {
                GenDraw.DrawRadiusRing(this.DrawPos.ToIntVec3(), 25.9f);
                base.DrawExtraSelectionOverlays();
            }

            public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
            {
                ReEnterBuildingDesignator();
                base.Destroy(mode);
            }

            public override void DeSpawn()
            {
                ReEnterBuildingDesignator();
                base.DeSpawn();
            }

            public override void Discard()
            {
                ReEnterBuildingDesignator();
                base.Discard();
            }

            private void ReEnterBuildingDesignator()
            {
                List<Designator> resolved = (resolvedDesignatorInfo.GetValue(this.def.designationCategory) as List<Designator>);
                resolved.Add(new Designator_Build(this.def));
                resolvedDesignatorInfo.SetValue(this.def.designationCategory, resolved);
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(base.GetInspectString());
                sb.AppendLine("Repo favors: " + BehaviourInterpreter.staticVariables.repoCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_BOB : Building
        {
            public override IEnumerable<Gizmo> GetGizmos()
            {
                foreach (Gizmo g in base.GetGizmos())
                    yield return g;

                if (BehaviourInterpreter.staticVariables.bobCount > 0)
                {
                    Command_Action build = new Command_Action()
                    {
                        defaultLabel = "Bob's Creation",
                        defaultDesc = "Channel Bob's favor.",
                        icon = ContentFinder<Texture2D>.Get("Things/Mote/FeedbackEquip", true),
                        activateSound = SoundDefOf.Click
                    };
                    build.action = delegate
                    {
                        Find.Targeter.BeginTargeting(new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(Find.VisibleMap) || !c.IsValid || !c.Cell.InBounds(Find.VisibleMap) || !c.Cell.Standable(Find.VisibleMap))
                                {
                                    return false;
                                }
                                return Mathf.Sqrt(c.Cell.DistanceToSquared(this.Position)) < 26f;
                            },
                            canTargetLocations = true
                        }, lti =>
                        {
                            Thing wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.BlocksGranite);
                            GenSpawn.Spawn(wall, lti.Cell, Find.VisibleMap);
                            wall.SetFaction(Faction.OfPlayer);

                            BehaviourInterpreter.staticVariables.bobCount--;
                            if (BehaviourInterpreter.staticVariables.bobCount > 0)
                                BehaviourInterpreter._instance.WaitAndExecute(() => build.action());
                        }, null, null, build.icon);
                    };
                    yield return build;
                }
            }

            public override void DrawExtraSelectionOverlays()
            {
                GenDraw.DrawRadiusRing(this.DrawPos.ToIntVec3(), 25.9f);
                base.DrawExtraSelectionOverlays();
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(base.GetInspectString());
                sb.AppendLine("Bob favors: " + BehaviourInterpreter.staticVariables.bobCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_ROOTSY : Building
        {
            public override IEnumerable<Gizmo> GetGizmos()
            {
                foreach (Gizmo g in base.GetGizmos())
                    yield return g;

                if (BehaviourInterpreter.staticVariables.rootsyCount > 0)
                {
                    Command_Action grower = new Command_Action()
                    {
                        defaultLabel = "Rootsy's  Favor",
                        defaultDesc = "Channel Rootsy's favor.",
                        icon = ContentFinder<Texture2D>.Get("Things/Mote/Sow", true),
                        activateSound = SoundDefOf.Click
                    };
                    grower.action = delegate
                    {
                        Find.Targeter.BeginTargeting(new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(Find.VisibleMap) || !c.IsValid || !c.Cell.InBounds(Find.VisibleMap))
                                {
                                    return false;
                                }
                                List<IntVec3> cells = GenAdj.CellsOccupiedBy(c.Cell, Rot4.North, new IntVec2(5, 5)).ToList();

                                bool returns = false;
                                foreach (IntVec3 current in cells)
                                {
                                    if (current.InBounds(Find.VisibleMap))
                                        if (current.GetThingList(Find.VisibleMap).Any(t => t is Plant))
                                            returns = true;
                                }
                                if (returns)
                                    GenDraw.DrawFieldEdges(cells);
                                return returns;
                            },
                            canTargetLocations = true
                        }, c =>
                        {
                            foreach (IntVec3 current in GenAdj.CellsOccupiedBy(c.Cell, Rot4.North, new IntVec2(6, 6)))
                            {
                                if (current.IsValid && current.InBounds(Find.VisibleMap))
                                {
                                    IEnumerable<Plant> list = current.GetThingList(Find.VisibleMap).Where(t => t is Plant).Cast<Plant>();
                                    foreach (Plant current2 in list)
                                    {
                                        current2.Growth = 1f;
                                        current2.Map.mapDrawer.MapMeshDirty(current2.Position, MapMeshFlag.Things);
                                    }
                                    MoteMaker.ThrowMetaPuff(current.ToVector3(), Find.VisibleMap);
                                }
                            }
                            BehaviourInterpreter.staticVariables.rootsyCount--;
                            if (BehaviourInterpreter.staticVariables.rootsyCount > 0)
                                BehaviourInterpreter._instance.WaitAndExecute(() => grower.action());  //LongEventHandler.QueueLongEvent(() => extinguish.action(), "", false, null);
                        }, null, null, grower.icon);
                    };

                    yield return grower;
                }
            }
            
            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(base.GetInspectString());
                sb.AppendLine("Rootsy favors: " + BehaviourInterpreter.staticVariables.rootsyCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_HUMOUR : Building
        {
            static FieldInfo resolvedDesignatorInfo = typeof(DesignationCategoryDef).GetField("resolvedDesignators", BindingFlags.Instance | BindingFlags.NonPublic);

            public override void SpawnSetup(Map map, bool respawningAfterLoad)
            {
                base.SpawnSetup(map, respawningAfterLoad);
                resolvedDesignatorInfo.SetValue(this.def.designationCategory, (resolvedDesignatorInfo.GetValue(this.def.designationCategory) as List<Designator>).Where(d => !(d is Designator_Build build) || !build.PlacingDef.Equals(this.def)).ToList());
            }

            public override IEnumerable<Gizmo> GetGizmos()
            {
                foreach (Gizmo g in base.GetGizmos())
                    yield return g;

                if (BehaviourInterpreter.staticVariables.humourCount > 0)
                {
                    Command_Action healer = new Command_Action()
                    {
                        defaultLabel = "Humour's heal",
                        defaultDesc = "Channel Humour's favor.",
                        icon = ContentFinder<Texture2D>.Get("UI/Icons/ColonistBar/MedicalRest", true),
                        activateSound = SoundDefOf.Click
                    };
                    healer.action = delegate
                    {
                        Find.Targeter.BeginTargeting(new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(Find.VisibleMap) || !c.IsValid || !c.Cell.InBounds(Find.VisibleMap))
                                {
                                    return false;
                                }
                                Pawn pawn = c.Cell.GetFirstPawn(c.Map);
                                return (pawn?.IsColonistPlayerControlled ?? false) && pawn.health.hediffSet.GetHediffs<Hediff_Injury>().Count(hi => !hi.IsOld()) > 0 && Mathf.Sqrt(c.Cell.DistanceToSquared(this.Position)) < 26f;
                            },
                            canTargetLocations = false,
                            canTargetPawns = true
                        }, lti =>
                        {
                            Pawn p = lti.Thing as Pawn;

                            int i = 20;

                            List<Hediff_Injury> hediffs = p.health.hediffSet.GetHediffs<Hediff_Injury>().Where(hi => !hi.IsOld()).ToList();
                            while (i > 0 && hediffs.Count > 0)
                            {
                                Hediff_Injury hediff = hediffs.First();
                                float val = Mathf.Min(0, hediff.Severity);
                                i -= Mathf.RoundToInt(val);
                                hediff.Heal(val);
                                hediffs.Remove(hediff);
                            }

                            BehaviourInterpreter.staticVariables.humourCount--;
                            if (BehaviourInterpreter.staticVariables.humourCount > 0)
                                BehaviourInterpreter._instance.WaitAndExecute(() => healer.action());  //LongEventHandler.QueueLongEvent(() => extinguish.action(), "", false, null);
                        }, null, null, healer.icon);
                    };

                    yield return healer;


                    Command_Action immunity = new Command_Action()
                    {
                        defaultLabel = "Humour's immunity boost",
                        defaultDesc = "Channel Humour's favor.",
                        icon = ContentFinder<Texture2D>.Get("UI/Icons/ColonistBar/MedicalRest", true),
                        activateSound = SoundDefOf.Click
                    };
                    immunity.action = delegate
                    {
                        Find.Targeter.BeginTargeting(new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(Find.VisibleMap) || !c.IsValid || !c.Cell.InBounds(Find.VisibleMap))
                                {
                                    return false;
                                }
                                Pawn pawn = c.Cell.GetFirstPawn(c.Map);
                                return (pawn?.IsColonistPlayerControlled ?? false) && pawn.health.hediffSet.hediffs.Any(hd => hd.TryGetComp<HediffComp_Immunizable>() != null) && Mathf.Sqrt(c.Cell.DistanceToSquared(this.Position)) < 26f;
                            },
                            canTargetLocations = false,
                            canTargetPawns = true
                        }, lti =>
                        {
                            Pawn p = lti.Thing as Pawn;

                            Hediff hediff = p.health.hediffSet.hediffs.Where(hd => hd.TryGetComp<HediffComp_Immunizable>() != null).First();

                            p.health.immunity.GetImmunityRecord(hediff.def).immunity += 0.10f;


                            BehaviourInterpreter.staticVariables.humourCount--;
                            if (BehaviourInterpreter.staticVariables.humourCount > 0)
                                BehaviourInterpreter._instance.WaitAndExecute(() => immunity.action());
                        }, null, null, immunity.icon);
                    };

                    yield return immunity;

                    if (BehaviourInterpreter.staticVariables.humourCount >= 10)
                    {
                        Command_Action revive = new Command_Action()
                        {
                            defaultLabel = "Humour's revive",
                            defaultDesc = "Channel Humour's favor.",
                            icon = ContentFinder<Texture2D>.Get("UI/Icons/ColonistBar/MedicalRest", true),
                            activateSound = SoundDefOf.Click
                        };
                        revive.action = delegate
                        {
                            Find.Targeter.BeginTargeting(new TargetingParameters()
                            {
                                validator = c =>
                                {
                                    if (c.Cell.Fogged(Find.VisibleMap) || !c.IsValid || !c.Cell.InBounds(Find.VisibleMap))
                                    {
                                        return false;
                                    }
                                    Corpse corpse = c.Cell.GetThingList(c.Map).Find(t => t is Corpse) as Corpse;
                                    return corpse != null && corpse.GetRotStage() == RotStage.Fresh && corpse.InnerPawn.IsColonist;
                                },
                                canTargetLocations = true
                            }, lti =>
                            {
                                Corpse corpse = lti.Cell.GetThingList(Find.VisibleMap).Find(t => t is Corpse) as Corpse;
                                Pawn pawn = corpse.InnerPawn;
                                corpse.Destroy();
                                typeof(Thing).GetField("mapIndexOrState", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pawn, (sbyte)-1);

                                pawn.health.Reset();
                                pawn.workSettings = new Pawn_WorkSettings(pawn);
                                pawn.mindState = new Pawn_MindState(pawn);
                                pawn.carryTracker = new Pawn_CarryTracker(pawn);
                                pawn.needs = new Pawn_NeedsTracker(pawn);
                                pawn.trader = new Pawn_TraderTracker(pawn);

                                typeof(Pawn_HealthTracker).GetField("healthState", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pawn.health, PawnHealthState.Mobile);
                                if (pawn.Faction != Faction.OfPlayer)
                                    pawn.SetFaction(Faction.OfPlayer);
                                pawn.workSettings.EnableAndInitialize();
                                GenSpawn.Spawn(pawn, lti.Cell, Find.VisibleMap);

                                BehaviourInterpreter.staticVariables.humourCount -= 10;
                                if (BehaviourInterpreter.staticVariables.humourCount > 10)
                                    BehaviourInterpreter._instance.WaitAndExecute(() => revive.action());
                            }, null, null, revive.icon);
                        };

                        yield return revive;
                    }
                }
            }

            public override void DrawExtraSelectionOverlays()
            {
                GenDraw.DrawRadiusRing(this.DrawPos.ToIntVec3(), 25.9f);
                base.DrawExtraSelectionOverlays();
            }

            public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
            {
                ReEnterBuildingDesignator();
                base.Destroy(mode);
            }

            public override void DeSpawn()
            {
                ReEnterBuildingDesignator();
                base.DeSpawn();
            }

            public override void Discard()
            {
                ReEnterBuildingDesignator();
                base.Discard();
            }

            private void ReEnterBuildingDesignator()
            {
                List<Designator> resolved = (resolvedDesignatorInfo.GetValue(this.def.designationCategory) as List<Designator>);
                resolved.Add(new Designator_Build(this.def));
                resolvedDesignatorInfo.SetValue(this.def.designationCategory, resolved);
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(base.GetInspectString());
                sb.AppendLine("Humour favors: " + BehaviourInterpreter.staticVariables.humourCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_DORF : Building
        {
            public override IEnumerable<Gizmo> GetGizmos()
            {
                foreach (Gizmo g in base.GetGizmos())
                    yield return g;

                if (BehaviourInterpreter.staticVariables.dorfCount > 0)
                {
                    Command_Action miner = new Command_Action()
                    {
                        defaultLabel = "Dorf's  Favor",
                        defaultDesc = "Channel Rootsy's favor.",
                        icon = ContentFinder<Texture2D>.Get("UI_dorf", true),
                        activateSound = SoundDefOf.Click
                    };

                    miner.action = delegate
                    {
                        Find.Targeter.BeginTargeting(new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (!c.IsValid || !c.Cell.InBounds(Find.VisibleMap))
                                {
                                    return false;
                                }
                                List<IntVec3> cells = GenAdj.CellsOccupiedBy(c.Cell, Rot4.North, new IntVec2(3, 3)).ToList();

                                bool returns = false;
                                foreach (IntVec3 current in cells)
                                {
                                    if (current.InBounds(Find.VisibleMap))
                                        if (current.GetThingList(Find.VisibleMap).Any(t => t.def.mineable))
                                            returns = true;
                                }
                                if (returns)
                                    GenDraw.DrawFieldEdges(cells);
                                return returns;
                            },
                            canTargetLocations = true
                        }, c =>
                        {
                            Pawn pawn = Map.mapPawns.FreeColonists.MaxBy(p => p.GetStatValue(StatDefOf.MiningYield));
                            foreach (IntVec3 current in GenAdj.CellsOccupiedBy(c.Cell, Rot4.North, new IntVec2(3, 3)))
                            {
                                if (current.IsValid && current.InBounds(Find.VisibleMap))
                                {
                                    IEnumerable<Mineable> list = current.GetThingList(Find.VisibleMap).Where(t => t is Mineable).Cast<Mineable>();
                                    while(list.Count() > 0)
                                    {
                                        Mineable current2 = list.First();
                                        current2.TakeDamage(new DamageInfo(DamageDefOf.Mining, current2.HitPoints, -1, pawn));
                                    }
                                    MoteMaker.ThrowMetaPuff(current.ToVector3(), Find.VisibleMap);
                                }
                            }
                            BehaviourInterpreter.staticVariables.dorfCount--;
                            if (BehaviourInterpreter.staticVariables.dorfCount > 0)
                                BehaviourInterpreter._instance.WaitAndExecute(() => miner.action());
                        }, null, null, miner.icon);
                    };

                    yield return miner;
                }
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(base.GetInspectString());
                sb.AppendLine("Dorf favors: " + BehaviourInterpreter.staticVariables.dorfCount);
                return sb.ToString().Trim();
            }
        }
    }
}