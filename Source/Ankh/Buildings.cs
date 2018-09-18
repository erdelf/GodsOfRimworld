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
        public class Building_Zap : Building, IAttackTargetSearcher
        {
            public Thing Thing => this;

            public Verb CurrentEffectiveVerb => throw new NotImplementedException();

            public LocalTargetInfo LastAttackedTarget => throw new NotImplementedException();

            public int LastAttackTargetTick => throw new NotImplementedException();

            public override void TickRare()
            {
                List<IAttackTarget> potentialTargetsFor = this.Map.attackTargetsCache.GetPotentialTargetsFor(th: this);
                LocalTargetInfo target = GenClosest.ClosestThing_Global(center: this.Position, searchSet: potentialTargetsFor, maxDistance: 25.9f, validator: t => !((t as Pawn)?.Downed ?? false));

                if (target.IsValid && BehaviourInterpreter.staticVariables.zapCount > 0)
                {
                    this.Map.weatherManager.eventHandler.AddEvent(newEvent: new WeatherEvent_LightningStrike(map: this.Map, forcedStrikeLoc: target.Cell));
                    BehaviourInterpreter.staticVariables.zapCount--;
                }
            }

            public override void DrawExtraSelectionOverlays()
            {
                GenDraw.DrawRadiusRing(center: this.DrawPos.ToIntVec3(), radius: 25.9f);
                base.DrawExtraSelectionOverlays();
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(value: base.GetInspectString());
                sb.AppendLine(value: "Zap favors: " + BehaviourInterpreter.staticVariables.zapCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_Therm : Building
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
                        icon = ContentFinder<Texture2D>.Get(itemPath: "Things/Building/Natural/SteamGeyser"),
                        activateSound = SoundDefOf.Click
                    };
                    therm.action = delegate
                    {
                        Find.Targeter.BeginTargeting(targetParams: new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(map: Find.CurrentMap) || !c.IsValid || !c.Cell.InBounds(map: Find.CurrentMap)) return false;
                                List<IntVec3> cells = GenAdj.CellsOccupiedBy(center: c.Cell, rotation: Rot4.North, size: ThingDefOf.GeothermalGenerator.Size).ToList();

                                foreach (IntVec3 current in cells)
                                    if(current.InBounds(map: Find.CurrentMap))
                                        if (!current.Standable(map: Find.CurrentMap) || current.GetFirstThing(map: Find.CurrentMap, def: ThingDefOf.SteamGeyser) != null || !current.GetTerrain(map: Find.CurrentMap).affordances.Contains(item: TerrainAffordanceDefOf.Heavy))
                                            return false;
                                bool returns = Find.CurrentMap.reachability.CanReachColony(c: c.Cell);
                                if (returns)
                                    GenDraw.DrawFieldEdges(cells: cells);
                                return returns;
                            },
                            canTargetLocations = true
                        }, action: lti =>
                        {
                            Thing geyser = GenSpawn.Spawn(def: ThingDefOf.SteamGeyser, loc: lti.Cell, map: Find.CurrentMap);

                            foreach (IntVec3 current in GenAdj.CellsOccupiedBy(t: geyser))
                                if (current.IsValid && current.InBounds(map: Find.CurrentMap))
                                    foreach (Thing current2 in Find.CurrentMap.thingGrid.ThingsAt(c: current))
                                        if (current2 is Plant || current2 is Filth)
                                            current2.Destroy();

                            BehaviourInterpreter.staticVariables.thermCount--;
                            if (BehaviourInterpreter.staticVariables.thermCount > 0) BehaviourInterpreter.instance.WaitAndExecute(action: () => therm.action());
                        }, caster: null, actionWhenFinished: null, mouseAttachment: therm.icon);
                    };
                    yield return therm;

                    Command_Action extinguish = new Command_Action()
                    {
                        defaultLabel = "Therm's  Respite",
                        defaultDesc = "Channel Therm's favor into an inferno to calm the flames.",
                        icon = ContentFinder<Texture2D>.Get(itemPath: "UI_Therm"),
                        activateSound = SoundDefOf.Click
                    };
                    extinguish.action = delegate
                    {
                        Find.Targeter.BeginTargeting(targetParams: new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(map: Find.CurrentMap) || !c.IsValid || !c.Cell.InBounds(map: Find.CurrentMap)) return false;
                                List<IntVec3> cells = GenAdj.CellsOccupiedBy(center: c.Cell, rotation: Rot4.North, size: new IntVec2(newX: 6,newZ: 6)).ToList();

                                bool returns = false;
                                foreach (IntVec3 current in cells)
                                    if (current.InBounds(map: Find.CurrentMap))
                                        if (current.GetFirstThing(map: Find.CurrentMap, def: ThingDefOf.Fire) != null)
                                            returns = true;
                                if (returns)
                                    GenDraw.DrawFieldEdges(cells: cells);
                                return returns;
                            },
                            canTargetLocations = true
                        }, action: c =>
                        {
                            foreach (IntVec3 current in GenAdj.CellsOccupiedBy(center: c.Cell, rotation: Rot4.North, size: new IntVec2(newX: 6,newZ: 6)))
                                if (current.IsValid && current.InBounds(map: Find.CurrentMap))
                                {
                                    Thing[] list = current.GetThingList(map: Find.CurrentMap).Where(predicate: t => t.def == ThingDefOf.Fire).ToArray();
                                    foreach(Thing current2 in list)
                                    {
                                        current2.Destroy();
                                        MoteMaker.ThrowMetaPuff(loc: current.ToVector3(), map: Find.CurrentMap);
                                    }
                                }

                            BehaviourInterpreter.staticVariables.thermCount--;
                            if (BehaviourInterpreter.staticVariables.thermCount > 0) BehaviourInterpreter.instance.WaitAndExecute(action: () => extinguish.action());  //LongEventHandler.QueueLongEvent(() => extinguish.action(), "", false, null);
                        }, caster: null, actionWhenFinished: null, mouseAttachment: extinguish.icon);
                    };

                    yield return extinguish;
                }
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(value: base.GetInspectString());
                sb.AppendLine(value: "Therm favors: " + BehaviourInterpreter.staticVariables.thermCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_Peg : Building
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
                        icon = ContentFinder<Texture2D>.Get(itemPath: "UI_peg"),
                        activateSound = SoundDefOf.Click
                    };
                    bomb.action = delegate
                    {
                        Find.Targeter.BeginTargeting(targetParams: new TargetingParameters()
                        {
                            validator = lti =>
                            {
                                if (lti.Cell.Fogged(map: Find.CurrentMap) || !lti.IsValid || !lti.Cell.InBounds(map: Find.CurrentMap) || Mathf.Sqrt(f: lti.Cell.DistanceToSquared(b: this.Position)) > 26f) return false;
                                int cells = GenRadial.NumCellsInRadius(radius: 3.9f);

                                bool returns = false;
                                for (int i = 0; i < cells; i++)
                                {
                                    IntVec3 current = lti.Cell + GenRadial.RadialPattern[i];
                                    if (current.InBounds(map: Find.CurrentMap))
                                        if (!current.GetThingList(map: Find.CurrentMap).NullOrEmpty())
                                            returns = true;
                                }
                                if (returns)
                                    GenDraw.DrawRadiusRing(center: lti.Cell, radius: 3.9f);
                                return returns;
                            },
                            canTargetPawns = true,
                            canTargetLocations = true
                        }, action: lti =>
                        {
                            GenExplosion.DoExplosion(center: lti.Cell, map: this.Map, radius: 3.9f, damType: DamageDefOf.Bomb, instigator: null);

                            BehaviourInterpreter.staticVariables.pegCount--;
                            if (BehaviourInterpreter.staticVariables.pegCount > 0)
                                BehaviourInterpreter.instance.WaitAndExecute(action: () => bomb.action());
                        }, caster: null, actionWhenFinished: null, mouseAttachment: bomb.icon);
                    };
                    yield return bomb;
                }
            }

            public override void DrawExtraSelectionOverlays()
            {
                GenDraw.DrawRadiusRing(center: this.DrawPos.ToIntVec3(), radius: 25.9f);
                base.DrawExtraSelectionOverlays();
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(value: base.GetInspectString());
                sb.AppendLine(value: "Peg favors: " + BehaviourInterpreter.staticVariables.pegCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_Repo : Building
        {

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
                        icon = ContentFinder<Texture2D>.Get(itemPath: "Things/Mote/FeedbackEquip"),
                        activateSound = SoundDefOf.Click
                    };
                    restore.action = delegate
                    {
                        Find.Targeter.BeginTargeting(targetParams: new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(map: Find.CurrentMap) || !c.IsValid || !c.Cell.InBounds(map: Find.CurrentMap)) return false;
                                return (c.Cell.GetFirstPawn(map: c.Map)?.IsColonistPlayerControlled ?? false) && Mathf.Sqrt(f: c.Cell.DistanceToSquared(b: this.Position)) < 26f;
                            },
                            canTargetLocations = false,
                            canTargetPawns = true
                        }, action: lti =>
                        {
                            Pawn p = (Pawn) lti.Thing;

                            MethodInfo missing = typeof(HealthCardUtility).GetMethod(name: "VisibleHediffs", bindingAttr: BindingFlags.Static | BindingFlags.NonPublic) ?? throw new ArgumentNullException();
                            BodyPartRecord record = null;

                            if (((IEnumerable<Hediff>)missing.Invoke(obj: null, parameters: new object[] { p, false })).Where(
                                predicate: h => h.GetType() == typeof(Hediff_MissingPart)).TryRandomElement(result: out Hediff result))
                            {
                                record = result.Part;
                                p.health.RestorePart(part: record);
                            }
                            else if (p.health.hediffSet.hediffs.Where(predicate: h => h is Hediff_Injury || h.IsPermanent()).TryRandomElement(result: out result))
                            {
                                record = result.Part;
                                result.Heal(amount: result.Severity + 1);
                            }
                            else
                            {
                                while(record == null)
                                    record = p.health.hediffSet.GetRandomNotMissingPart(damDef: DamageDefOf.Bullet);
                            }

                            DefDatabase<RecipeDef>.AllDefsListForReading.Where(predicate: rd => (rd.addsHediff?.addedPartProps?.betterThanNatural ?? false) &&
                                (rd.appliedOnFixedBodyParts?.Select(selector: bdp => bdp.defName).Contains(value: record.def.defName) ?? false)).TryRandomElement(result: out RecipeDef recipe);

                            if (recipe == null)
                                return;

                            recipe.Worker.ApplyOnPawn(pawn: p, part: record, billDoer: null, ingredients: recipe.fixedIngredientFilter.AllowedThingDefs.Select(selector: td => ThingMaker.MakeThing(def: td, stuff: td.MadeFromStuff ? GenStuff.DefaultStuffFor(bd: td) : null)).ToList(), bill: null);

                            BehaviourInterpreter.staticVariables.repoCount--;
                            if (BehaviourInterpreter.staticVariables.repoCount > 0)
                                BehaviourInterpreter.instance.WaitAndExecute(action: () => restore.action());
                        }, caster: null, actionWhenFinished: null, mouseAttachment: restore.icon);
                    };
                    yield return restore;
                }
            }

            public override void DrawExtraSelectionOverlays()
            {
                GenDraw.DrawRadiusRing(center: this.DrawPos.ToIntVec3(), radius: 25.9f);
                base.DrawExtraSelectionOverlays();
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(value: base.GetInspectString());
                sb.AppendLine(value: "Repo favors: " + BehaviourInterpreter.staticVariables.repoCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_Bob : Building
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
                        icon = ContentFinder<Texture2D>.Get(itemPath: "Things/Mote/FeedbackEquip"),
                        activateSound = SoundDefOf.Click
                    };
                    build.action = delegate
                    {
                        Find.Targeter.BeginTargeting(targetParams: new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(map: Find.CurrentMap) || !c.IsValid || !c.Cell.InBounds(map: Find.CurrentMap) || !c.Cell.Standable(map: Find.CurrentMap)) return false;
                                return Mathf.Sqrt(f: c.Cell.DistanceToSquared(b: this.Position)) < 26f;
                            },
                            canTargetLocations = true
                        }, action: lti =>
                        {
                            Thing wall = ThingMaker.MakeThing(def: ThingDefOf.Wall, stuff: ThingDefOf.BlocksGranite);
                            GenSpawn.Spawn(newThing: wall, loc: lti.Cell, map: Find.CurrentMap);
                            wall.SetFaction(newFaction: Faction.OfPlayer);

                            BehaviourInterpreter.staticVariables.bobCount--;
                            if (BehaviourInterpreter.staticVariables.bobCount > 0)
                                BehaviourInterpreter.instance.WaitAndExecute(action: () => build.action());
                        }, caster: null, actionWhenFinished: null, mouseAttachment: build.icon);
                    };
                    yield return build;
                }
            }

            public override void DrawExtraSelectionOverlays()
            {
                GenDraw.DrawRadiusRing(center: this.DrawPos.ToIntVec3(), radius: 25.9f);
                base.DrawExtraSelectionOverlays();
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(value: base.GetInspectString());
                sb.AppendLine(value: "Bob favors: " + BehaviourInterpreter.staticVariables.bobCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_Rootsy : Building
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
                        icon = ContentFinder<Texture2D>.Get(itemPath: "Things/Mote/Sow"),
                        activateSound = SoundDefOf.Click
                    };
                    grower.action = delegate
                    {
                        Find.Targeter.BeginTargeting(targetParams: new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(map: Find.CurrentMap) || !c.IsValid || !c.Cell.InBounds(map: Find.CurrentMap)) return false;
                                List<IntVec3> cells = GenAdj.CellsOccupiedBy(center: c.Cell, rotation: Rot4.North, size: new IntVec2(newX: 5, newZ: 5)).ToList();

                                bool returns = false;
                                foreach (IntVec3 current in cells)
                                    if (current.InBounds(map: Find.CurrentMap))
                                        if (current.GetThingList(map: Find.CurrentMap).Any(predicate: t => t is Plant))
                                            returns = true;
                                if (returns)
                                    GenDraw.DrawFieldEdges(cells: cells);
                                return returns;
                            },
                            canTargetLocations = true
                        }, action: c =>
                        {
                            foreach (IntVec3 current in GenAdj.CellsOccupiedBy(center: c.Cell, rotation: Rot4.North, size: new IntVec2(newX: 6, newZ: 6)))
                                if (current.IsValid && current.InBounds(map: Find.CurrentMap))
                                {
                                    IEnumerable<Plant> list = current.GetThingList(map: Find.CurrentMap).Where(predicate: t => t is Plant).Cast<Plant>();
                                    foreach (Plant current2 in list)
                                    {
                                        current2.Growth = 1f;
                                        current2.Map.mapDrawer.MapMeshDirty(loc: current2.Position, dirtyFlags: MapMeshFlag.Things);
                                    }
                                    MoteMaker.ThrowMetaPuff(loc: current.ToVector3(), map: Find.CurrentMap);
                                }

                            BehaviourInterpreter.staticVariables.rootsyCount--;
                            if (BehaviourInterpreter.staticVariables.rootsyCount > 0)
                                BehaviourInterpreter.instance.WaitAndExecute(action: () => grower.action());  //LongEventHandler.QueueLongEvent(() => extinguish.action(), "", false, null);
                        }, caster: null, actionWhenFinished: null, mouseAttachment: grower.icon);
                    };

                    yield return grower;
                }
            }
            
            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(value: base.GetInspectString());
                sb.AppendLine(value: "Rootsy favors: " + BehaviourInterpreter.staticVariables.rootsyCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_Humour : Building
        {
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
                        icon = ContentFinder<Texture2D>.Get(itemPath: "UI/Icons/ColonistBar/MedicalRest"),
                        activateSound = SoundDefOf.Click
                    };
                    healer.action = delegate
                    {
                        Find.Targeter.BeginTargeting(targetParams: new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(map: Find.CurrentMap) || !c.IsValid || !c.Cell.InBounds(map: Find.CurrentMap)) return false;
                                Pawn pawn = c.Cell.GetFirstPawn(map: c.Map);
                                return (pawn?.IsColonistPlayerControlled ?? false) && pawn.health.hediffSet.GetHediffs<Hediff_Injury>().Count(predicate: hi => !hi.IsPermanent()) > 0 && Mathf.Sqrt(f: c.Cell.DistanceToSquared(b: this.Position)) < 26f;
                            },
                            canTargetLocations = false,
                            canTargetPawns = true
                        }, action: lti =>
                        {
                            Pawn p = (Pawn) lti.Thing;

                            int i = 20;

                            List<Hediff_Injury> hediffs = p.health.hediffSet.GetHediffs<Hediff_Injury>().Where(predicate: hi => !hi.IsPermanent()).ToList();
                            while (i > 0 && hediffs.Count > 0)
                            {
                                Hediff_Injury hediff = hediffs.First();
                                float val = Mathf.Min(a: i, b: hediff.Severity);
                                i -= Mathf.RoundToInt(f: val);
                                hediff.Heal(amount: val);
                                hediffs.Remove(item: hediff);
                            }

                            BehaviourInterpreter.staticVariables.humourCount--;
                            if (BehaviourInterpreter.staticVariables.humourCount > 0)
                                BehaviourInterpreter.instance.WaitAndExecute(action: () => healer.action());  //LongEventHandler.QueueLongEvent(() => extinguish.action(), "", false, null);
                        }, caster: null, actionWhenFinished: null, mouseAttachment: healer.icon);
                    };

                    yield return healer;


                    Command_Action immunity = new Command_Action()
                    {
                        defaultLabel = "Humour's immunity boost",
                        defaultDesc = "Channel Humour's favor.",
                        icon = ContentFinder<Texture2D>.Get(itemPath: "UI/Icons/ColonistBar/MedicalRest"),
                        activateSound = SoundDefOf.Click
                    };
                    immunity.action = delegate
                    {
                        Find.Targeter.BeginTargeting(targetParams: new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (c.Cell.Fogged(map: Find.CurrentMap) || !c.IsValid || !c.Cell.InBounds(map: Find.CurrentMap)) return false;
                                Pawn pawn = c.Cell.GetFirstPawn(map: c.Map);
                                return (pawn?.IsColonistPlayerControlled ?? false) && pawn.health.hediffSet.hediffs.Any(predicate: hd => hd.TryGetComp<HediffComp_Immunizable>() != null) && Mathf.Sqrt(f: c.Cell.DistanceToSquared(b: this.Position)) < 26f;
                            },
                            canTargetLocations = false,
                            canTargetPawns = true
                        }, action: lti =>
                        {
                            Pawn p = (Pawn) lti.Thing;

                            Hediff hediff = p.health.hediffSet.hediffs.First(hd => hd.TryGetComp<HediffComp_Immunizable>() != null);

                            p.health.immunity.GetImmunityRecord(def: hediff.def).immunity += 0.10f;


                            BehaviourInterpreter.staticVariables.humourCount--;
                            if (BehaviourInterpreter.staticVariables.humourCount > 0)
                                BehaviourInterpreter.instance.WaitAndExecute(action: () => immunity.action());
                        }, caster: null, actionWhenFinished: null, mouseAttachment: immunity.icon);
                    };

                    yield return immunity;

                    if (BehaviourInterpreter.staticVariables.humourCount >= 10)
                    {
                        Command_Action revive = new Command_Action()
                        {
                            defaultLabel = "Humour's revive",
                            defaultDesc = "Channel Humour's favor.",
                            icon = ContentFinder<Texture2D>.Get(itemPath: "UI/Icons/ColonistBar/MedicalRest"),
                            activateSound = SoundDefOf.Click
                        };
                        revive.action = delegate
                        {
                            Find.Targeter.BeginTargeting(targetParams: new TargetingParameters()
                            {
                                validator = c =>
                                {
                                    if (c.Cell.Fogged(map: Find.CurrentMap) || !c.IsValid || !c.Cell.InBounds(map: Find.CurrentMap)) return false;
                                    return c.Cell.GetThingList(map: c.Map).Find(match: t => t is Corpse) is Corpse corpse && corpse.GetRotStage() == RotStage.Fresh && corpse.InnerPawn.IsColonist;
                                },
                                canTargetLocations = true
                            }, action: lti =>
                            {
                                Corpse corpse = (Corpse) lti.Cell.GetThingList(map: Find.CurrentMap).Find(match: t => t is Corpse);
                                ResurrectionUtility.Resurrect(pawn: corpse.InnerPawn);

                                BehaviourInterpreter.staticVariables.humourCount -= 10;
                                if (BehaviourInterpreter.staticVariables.humourCount > 10)
                                    BehaviourInterpreter.instance.WaitAndExecute(action: () => revive.action());
                            }, caster: null, actionWhenFinished: null, mouseAttachment: revive.icon);
                        };

                        yield return revive;
                    }
                }
            }

            public override void DrawExtraSelectionOverlays()
            {
                GenDraw.DrawRadiusRing(center: this.DrawPos.ToIntVec3(), radius: 25.9f);
                base.DrawExtraSelectionOverlays();
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(value: base.GetInspectString());
                sb.AppendLine(value: "Humour favors: " + BehaviourInterpreter.staticVariables.humourCount);
                return sb.ToString().Trim();
            }
        }
        public class Building_Dorf : Building
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
                        icon = ContentFinder<Texture2D>.Get(itemPath: "UI_dorf"),
                        activateSound = SoundDefOf.Click
                    };

                    miner.action = delegate
                    {
                        Find.Targeter.BeginTargeting(targetParams: new TargetingParameters()
                        {
                            validator = c =>
                            {
                                if (!c.IsValid || !c.Cell.InBounds(map: Find.CurrentMap)) return false;
                                List<IntVec3> cells = GenAdj.CellsOccupiedBy(center: c.Cell, rotation: Rot4.North, size: new IntVec2(newX: 3, newZ: 3)).ToList();

                                bool returns = false;
                                foreach (IntVec3 current in cells)
                                    if (current.InBounds(map: Find.CurrentMap))
                                        if (current.GetThingList(map: Find.CurrentMap).Any(predicate: t => t.def.mineable))
                                            returns = true;
                                if (returns)
                                    GenDraw.DrawFieldEdges(cells: cells);
                                return returns;
                            },
                            canTargetLocations = true
                        }, action: c =>
                        {
                            Pawn pawn = this.Map.mapPawns.FreeColonists.MaxBy(selector: p => p.GetStatValue(stat: StatDefOf.MiningYield));
                            foreach (IntVec3 current in GenAdj.CellsOccupiedBy(center: c.Cell, rotation: Rot4.North, size: new IntVec2(newX: 3, newZ: 3)))
                                if (current.IsValid && current.InBounds(map: Find.CurrentMap))
                                {
                                    IEnumerable<Mineable> list = current.GetThingList(map: Find.CurrentMap).Where(predicate: t => t is Mineable).Cast<Mineable>().ToList();
                                    int i = 20;
                                    while (list.Any() && i > 0)
                                    {
                                        i--;
                                        Mineable mineable = list.First();
                                        mineable.TakeDamage(dinfo: new DamageInfo(def: DamageDefOf.Mining, amount: mineable.HitPoints, armorPenetration: -1, angle: -1, instigator: pawn));
                                    }

                                    MoteMaker.ThrowMetaPuff(loc: current.ToVector3(), map: Find.CurrentMap);
                                }

                            BehaviourInterpreter.staticVariables.dorfCount--;
                            if (BehaviourInterpreter.staticVariables.dorfCount > 0)
                                BehaviourInterpreter.instance.WaitAndExecute(action: () => miner.action());
                        }, caster: null, actionWhenFinished: null, mouseAttachment: miner.icon);
                    };

                    yield return miner;
                }
            }

            public override string GetInspectString()
            {
                StringBuilder sb = new StringBuilder(value: base.GetInspectString());
                sb.AppendLine(value: "Dorf favors: " + BehaviourInterpreter.staticVariables.dorfCount);
                return sb.ToString().Trim();
            }
        }

        public class Building_Altar : Building
        {
            public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
            {
                if(BehaviourInterpreter.instance.instanceVariableHolder.altarState <= 1)
                    yield return new FloatMenuOption(label: "Sacrifice " + selPawn.LabelCap, action: () => selPawn.jobs.TryTakeOrderedJob(job: new Job(def: AnkhDefOf.sacrificeToAltar, targetA: this)));
            }

            public override IEnumerable<Gizmo> GetGizmos()
            {
                Find.ReverseDesignatorDatabase.AllDesignators.Clear();
                BehaviourInterpreter.instance.WaitAndExecute(action: () => Find.ReverseDesignatorDatabase.Reinit());
                return new List<Gizmo>();
            }
        }
    }
}