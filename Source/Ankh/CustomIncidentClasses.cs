using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Ankh
{
    internal static class CustomIncidentClasses
    {
        private static readonly MethodInfo raidPointInfo = typeof(IncidentWorker_Raid).GetMethod(name: "ResolveRaidPoints", bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo raidFactionInfo = typeof(IncidentWorker_RaidEnemy).GetMethod(name: "TryResolveRaidFaction", bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo raidStrategyInfo = typeof(IncidentWorker_RaidEnemy).GetMethod(name: "ResolveRaidStrategy", bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo raidArriveInfo = typeof(IncidentWorker_Raid).GetMethod(name: "ResolveRaidArriveMode", bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo raidSpawnInfo = typeof(IncidentWorker_Raid).GetMethod(name: "ResolveRaidSpawnCenter", bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance);

        public static void CallEnemyRaid(IncidentParms parms, Letter letter)
        {
            object obj = IncidentDefOf.RaidEnemy.Worker;
            Map map = (Map)parms.target;
            parms.points = Rand.Range(min: 50f, max: 300f);
            raidPointInfo.Invoke(obj: obj, parameters: new object[] { parms });
            if (!(bool)raidFactionInfo.Invoke(obj: obj, parameters: new object[] { parms })) throw new Exception();
            raidStrategyInfo.Invoke(obj: obj, parameters: new object[] { parms });
            raidArriveInfo.Invoke(obj: obj, parameters: new object[] { parms });
            raidSpawnInfo.Invoke(obj: obj, parameters: new object[] { parms });
            PawnGroupMakerParms defaultPawnGroupMakerParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(groupKind: PawnGroupKindDefOf.Combat, parms: parms);
            List<Pawn> list = PawnGroupMakerUtility.GeneratePawns(parms: defaultPawnGroupMakerParms).ToList();
            if (list.Count == 0) throw new Exception();
            TargetInfo target = TargetInfo.Invalid;
            if (parms.raidArrivalMode == PawnsArrivalModeDefOf.CenterDrop || parms.raidArrivalMode == PawnsArrivalModeDefOf.EdgeDrop)
            {
                DropPodUtility.DropThingsNear(dropCenter: parms.spawnCenter, map: map, things: list.Cast<Thing>(), openDelay: parms.podOpenDelay, canInstaDropDuringInit: false, leaveSlag: true);
                target = new TargetInfo(cell: parms.spawnCenter, map: map);
            }
            else
            {
                foreach (Pawn current in list)
                {
                    IntVec3 loc = CellFinder.RandomClosewalkCellNear(root: parms.spawnCenter, map: map, radius: 8);
                    GenSpawn.Spawn(newThing: current, loc: loc, map: map);
                    target = current;
                }
            }
            parms.raidStrategy.Worker.MakeLords(parms: parms, pawns: list);
            AvoidGridMaker.RegenerateAvoidGridsFor(faction: parms.faction, map: map);

            if (letter != null)
            {
                letter.lookTargets = new LookTargets(target);
                Find.LetterStack.ReceiveLetter(let: letter);
            }

            Find.TickManager.slower.SignalForceNormalSpeedShort();
            Find.StoryWatcher.statsRecord.numRaidsEnemy++;
        }

/*
        public static void CallBlight(Letter letter)
        {
            Map map = Find.AnyPlayerHomeMap;
            List<Thing> list = map.listerThings.ThingsInGroup(group: ThingRequestGroup.Plant);
            bool flag = false;

            GlobalTargetInfo target = GlobalTargetInfo.Invalid;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                Plant plant = (Plant)list[index: i];
                if (Math.Abs(map.Biome.CommonalityOfPlant(plantDef: plant.def)) < 0.001f)
                    if (plant.def.plant.growDays <= 16f)
                        if (plant.LifeStage == PlantLifeStage.Growing || plant.LifeStage == PlantLifeStage.Mature)
                        {
                            plant.CropBlighted();
                            flag = true;
                            if (Rand.Bool)
                                target = plant;
                        }
            }
            if (!flag) throw new Exception();

            if (letter != null)
            {
                letter.lookTargets = new LookTargets(target);
                Find.LetterStack.ReceiveLetter(let: letter);
            }
        }
*/

/*
        public static void CallManhunter(IncidentParms parms, Letter letter)
        {
            Map map = (Map)parms.target;
            if (!DefDatabase<PawnKindDef>.AllDefsListForReading.Where(predicate: p => p.RaceProps.Animal).ToList().TryRandomElement(result: out PawnKindDef pawnKindDef)) throw new Exception();
            if (!RCellFinder.TryFindRandomPawnEntryCell(result: out IntVec3 root, map: map, roadChance: 50f)) throw new Exception();
            List<Pawn> list = ManhunterPackIncidentUtility.GenerateAnimals(animalKind: pawnKindDef, tile: map.Tile, points: parms.points);
            for (int i = 0; i < list.Count; i++)
            {
                Pawn pawn = list[index: i];
                IntVec3 loc = CellFinder.RandomClosewalkCellNear(root: root, map: map, radius: 10);
                GenSpawn.Spawn(newThing: pawn, loc: loc, map: map);
                pawn.mindState.mentalStateHandler.TryStartMentalState(stateDef: MentalStateDefOf.ManhunterPermanent);
                pawn.mindState.exitMapAfterTick = Find.TickManager.TicksGame + Rand.Range(min: GenDate.TicksPerHour, max: GenDate.TicksPerDay/2);
            }
            if (letter != null)
            {
                letter.lookTargets = new LookTargets(t: list[index: 0]);
                Find.LetterStack.ReceiveLetter(let: letter);
            }
            Find.TickManager.slower.SignalForceNormalSpeedShort();
        }
*/

/*
        public static void CallShipPart(Letter letter, int count = 1)
        {
            Map map = Find.AnyPlayerHomeMap;
            int num = 0;
            IntVec3 cell = IntVec3.Invalid;

            ThingDef shipPart = DefDatabase<ThingDef>.AllDefsListForReading.Where(predicate: x => x.defName.Contains(value: "Crashed") && x.defName.Contains(value: "ShipPart")).RandomElement();

            for (int i = 0; i < count; i++)
            {
                Predicate<IntVec3> validator = delegate (IntVec3 c)
                {
                    if (c.Fogged(map: map)) return false;
                    foreach (IntVec3 current in GenAdj.CellsOccupiedBy(center: c, rotation: Rot4.North, size: shipPart.size))
                    {
                        if (!current.Standable(map: map)) return false;
                        if (map.roofGrid.Roofed(c: current)) return false;
                    }
                    return map.reachability.CanReachColony(c: c);
                };
                if (!CellFinder.TryFindRandomCellNear(root: CellFinder.RandomCell(map: map), map: map, squareRadius: 200, validator: validator, result: out IntVec3 intVec))
                    if (!CellFinderLoose.TryFindRandomNotEdgeCellWith(minEdgeDistance: 14, validator: validator, map: map, result: out intVec))
                        break;

                GenExplosion.DoExplosion(center: intVec, map: map, radius: 3f, damType: DamageDefOf.Flame, instigator: null);
                Building buildingCrashedShipPart = (Building)GenSpawn.Spawn(def: shipPart, loc: intVec, map: map);
                buildingCrashedShipPart.SetFaction(newFaction: Faction.OfMechanoids);

                StorytellerComp storytellerComp = Find.Storyteller.storytellerComps.First(predicate: x => x is StorytellerComp_OnOffCycle || x is StorytellerComp_RandomMain);
                IncidentParms incidentParms = storytellerComp.GenerateParms(incCat: IncidentCategoryDefOf.ThreatBig, target: Find.CurrentMap);

                incidentParms.points *= 0.9f;

                if (incidentParms.points < 300f) incidentParms.points = 300f;

                shipPart.thingClass.GetField(name: "pointsLeft").SetValue(obj: buildingCrashedShipPart, value: incidentParms.points);

                num++;
                cell = intVec;
            }
            if (num > 0)
            {
                if (map == Find.CurrentMap) Find.CameraDriver.shaker.DoShake(mag: 1f);

                if (letter != null)
                {
                    letter.lookTargets = new LookTargets(new TargetInfo(cell: cell, map: map));
                    Find.LetterStack.ReceiveLetter(let: letter);
                }

            }
            else
            {
                throw new Exception();
            }
        }
*/

        public class PawnGroupKindWorker_Wrath : PawnGroupKindWorker_Normal
        {

            protected override void GeneratePawns(PawnGroupMakerParms parms, PawnGroupMaker groupMaker, List<Pawn> outPawns, bool errorOnZeroResults = true)
            {
                bool allowFood = parms.raidStrategy == null || parms.raidStrategy.pawnsCanBringFood;
                bool forceIncapDone = false;
                float points = parms.points;
                while (points > 0)
                {
                    Pawn p = PawnGenerator.GeneratePawn(request: new PawnGenerationRequest(kind: PawnKindDefOf.AncientSoldier, faction: parms.faction, context: PawnGenerationContext.NonPlayer, tile: parms.tile, forceGenerateNewPawn: true, newborn: false, allowDead: false, allowDowned: false, canGeneratePawnRelations: true, mustBeCapableOfViolence: true, colonistRelationChanceFactor: 1f, forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowFood: allowFood));
                    p.InitializeComps();
                    if (parms.forceOneIncap && !forceIncapDone)
                    {
                        p.health.forceIncap = true;
                        p.mindState.canFleeIndividual = false;
                        forceIncapDone = true;
                    }
                    points -= p.kindDef.combatPower;
                    outPawns.Add(item: p);
                }
            }
        }

        public class MapCondition_WrathBombing : GameCondition
        {
            private static readonly IntRange ticksBetweenStrikes = new IntRange(min: GenDate.TicksPerHour / 4, max: GenDate.TicksPerDay / 4);

            private int nextBombardmentStrike;

            public override void GameConditionTick()
            {
                if (Find.TickManager.TicksGame > this.nextBombardmentStrike)
                {
                    this.SingleMap.weatherManager.eventHandler.AddEvent(newEvent: new WeatherEvent_WrathBombing(map: this.SingleMap, faction: Faction.OfPlayer));
                    this.nextBombardmentStrike = Find.TickManager.TicksGame + ticksBetweenStrikes.RandomInRange;
                }
            }

            public override void End()
            {
                WeatherDef weatherDef = DefDatabase<WeatherDef>.AllDefsListForReading.Where(predicate: wd => wd.rainRate > 0.5f).RandomElement();
                this.SingleMap.weatherManager.TransitionTo(newWeather: weatherDef);
                (typeof(WeatherDecider).GetField(name: "curWeatherDuration", bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new NullReferenceException()).SetValue(obj: this.SingleMap.weatherDecider, value: weatherDef.durationRange.RandomInRange);
                base.End();
            }
        }

        public class WeatherEvent_WrathBombing : WeatherEvent_LightningFlash
        {
            public static bool erdelf;
            private readonly Faction faction;
            public WeatherEvent_WrathBombing(Map map, Faction faction) : base(map: map) => this.faction = faction;

            public override void FireEvent()
            {
                base.FireEvent();
                IntVec3 strikeLoc;

                if (this.faction == null || this.map.mapPawns.SpawnedPawnsInFaction(faction: this.faction).Count <= 0)
                    return;
                while (!CellFinder.TryFindRandomCellNear(root: this.map.mapPawns.SpawnedPawnsInFaction(faction: this.faction).RandomElement().Position, map: this.map, squareRadius: 100,
                    validator: (sq => sq.Standable(map: this.map)), result: out strikeLoc)) { }

                GenExplosion.DoExplosion(center: strikeLoc, map: this.map, radius: this.map.roofGrid.RoofAt(c: strikeLoc) == RoofDefOf.RoofRockThick ? erdelf ? 40f : 20f : erdelf ? 56f : 40f, damType: DamageDefOf.Flame, instigator: null,
                    explosionSound: SoundDefOf.PlanetkillerImpact);

                /*
                GenExplosion.DoExplosion(strikeLoc, map, map.roofGrid.RoofAt(strikeLoc) == RoofDefOf.RoofRockThick ? 5f : 10f, DamageDefOf.Flame, null,
                    SoundDefOf.PlanetkillerImpact, null, null, null, 0f, 1, true, null, 0f, 1);
                GenExplosion.DoExplosion(strikeLoc, map, map.roofGrid.RoofAt(strikeLoc) == RoofDefOf.RoofRockThick ? 5f : 10f, DamageDefOf.EMP, null,
                    SoundDefOf.PlanetkillerImpact, null, null, null, 0f, 1, true, null, 0f, 1);
                */

                this.map.roofGrid.SetRoof(c: strikeLoc, def: null);
                Vector3 loc = strikeLoc.ToVector3Shifted();
                MoteMaker.ThrowSmoke(loc: loc, map: this.map, size: 1.5f);
                MoteMaker.ThrowMicroSparks(loc: loc, map: this.map);
                MoteMaker.ThrowLightningGlow(loc: loc, map: this.map, size: 1.5f);
                MoteMaker.ThrowDustPuff(loc: loc, map: this.map, scale: 1.5f);
                MoteMaker.ThrowFireGlow(c: strikeLoc, map: this.map, size: 1.5f);
                MoteMaker.ThrowHeatGlow(c: strikeLoc, map: this.map, size: 1.5f);
            }
        }

        public class MiracleHeal : IncidentWorker
        {
            protected override bool CanFireNowSub(IncidentParms parms) => 
                ((Map)parms.target).mapPawns.FreeColonists.Any(predicate: col => col.health.hediffSet.GetHediffs<Hediff_Injury>().Any() && (col.Faction == Faction.OfPlayer));

            
            protected override bool TryExecuteWorker(IncidentParms parms)
            {
                List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder();
                if (pawns.Count > 0)
                {
                    while (pawns.Count > 7)
                        pawns.RemoveAt(index: Rand.Range(min: 0, max: pawns.Count));
                    pawns.ForEach(action: pawn =>
                    {
                        foreach (Hediff_Injury current in pawn.health.hediffSet.GetHediffs<Hediff_Injury>())
                            current.Heal(amount: (int)current.Severity + 1);
                    });
                    Find.LetterStack.ReceiveLetter(label: "Miracle Heal",
                            text: "The Gods healed your injuries!", textLetterDef: LetterDefOf.PositiveEvent);
                    return true;
                }
                return false;
            }
        }

        public class AltarAppearance : IncidentWorker
        {
            protected override bool CanFireNowSub(IncidentParms parms) => 
                !((Map)parms.target).listerThings.ThingsOfDef(def: AnkhDefOf.sacrificeAltar).Any();

            protected override bool TryExecuteWorker(IncidentParms parms)
            {

                Map map = (Map) parms.target;
                Thing thing;
                IntVec3 loc = map.AllCells.Where(predicate: ivc => ivc.Standable(map: map) && !ivc.Fogged(map: map) && ivc.GetTerrain(map: map).affordances.Contains(item: TerrainAffordanceDefOf.Heavy) && !ivc.CloseToEdge(map: map, edgeDist: Mathf.RoundToInt(f: map.Size.LengthHorizontal/4))).RandomElement();

                if (Find.CurrentMap != map)
                    Current.Game.CurrentMap = map;
                Find.CameraDriver.SetRootPosAndSize(rootPos: Find.CurrentMap.rememberedCameraPos.rootPos, rootSize: 50f);
                Find.CameraDriver.JumpToCurrentMapLoc(cell: loc);


                Find.LetterStack.ReceiveLetter(label: "Altar appeared", text: "An altar of the Gods appeared. They might have something to offer.", textLetterDef: LetterDefOf.PositiveEvent, 
                    lookTargets: thing = GenSpawn.Spawn(newThing: ThingMaker.MakeThing(def: AnkhDefOf.sacrificeAltar, stuff: GenStuff.RandomStuffFor(td: AnkhDefOf.sacrificeAltar)), loc: loc, map: map));

                CellRect occupied = thing.OccupiedRect();
                CellRect expanded = occupied.ExpandedBy(dist: 5);


                bool LocCheck(IntVec3 ivc)
                {
                    return ivc.Standable(map: map) && !ivc.Fogged(map: map) && !ivc.GetThingList(map: map).Any(predicate: t => t.Faction == Faction.OfPlayer) && !GenAdjFast.AdjacentCells8Way(root: ivc).Any(predicate: iv => iv.GetThingList(map: map).Any(predicate: t => t.Faction == Faction.OfPlayer)) && !expanded.IsOnEdge(c: ivc);
                }

                CellRect.CenteredOn(center: loc, radius: 10).Cells.Where(predicate: ivc => LocCheck(ivc: ivc) && !occupied.Contains(c: ivc)).ToList().ForEach(action: c =>
                {
                    map.weatherManager.eventHandler.AddEvent(newEvent: new WeatherEvent_LightningStrike(map: map, forcedStrikeLoc: c));
                    BehaviourInterpreter.instance.WaitAndExecute(action: () => GenAdjFast.AdjacentCells8Way(root: c).ForEach(action: ivc => ivc.GetThingList(map: map).ForEach(action: t =>
                    {
                        t.TakeDamage(dinfo: new DamageInfo(def: DamageDefOf.Extinguish, amount: 5000));
                        t.HitPoints = t.MaxHitPoints;
                    })));
                });

                if(!DefDatabase<TerrainDef>.AllDefsListForReading.Where(predicate: td => td.defName.IndexOf(value: thing.Stuff.stuffProps.stuffAdjective ?? thing.Stuff.defName.Replace(oldValue: "Log", newValue: "").Replace(oldValue: "Blocks",newValue: ""), comparisonType: StringComparison.OrdinalIgnoreCase) >= 0).TryRandomElement(result: out TerrainDef terrain))
                    terrain = TerrainDefOf.Concrete;
                
                expanded.Cells.ToList().ForEach(action: c =>
                {
                    if (!occupied.Contains(c: c))
                        c.GetThingList(map: map).ForEach(action: t => t.Destroy());
                    map.snowGrid.SetDepth(c: c, newDepth: 0f);
                    if (map.terrainGrid.CanRemoveTopLayerAt(c: c))
                        map.terrainGrid.RemoveTopLayer(c: c, doLeavings: false);
                    map.terrainGrid.SetTerrain(c: c, newTerr: terrain);
                });



                ThingDef sculpture = ThingDef.Named(defName: "SculptureGrand");


                FieldInfo subGraphicInfo = typeof(Graphic_Collection).GetField(name: "subGraphics", bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new ArgumentNullException();

                trysculptures:
                try
                {
                    Thing sculptureExample;
                    Graphic[] graphics = ((Graphic[])subGraphicInfo.GetValue(obj: (sculptureExample = GenSpawn.Spawn(newThing: ThingMaker.MakeThing(def: sculpture, stuff: thing.Stuff), loc: new IntVec3(newX: expanded.maxX - 1, newY: 0, newZ: expanded.maxZ - 1), map: map)).Graphic));
                    Graphic graphic = graphics[sculptureExample.thingIDNumber & graphics.Length];
                    (graphics = (Graphic[])subGraphicInfo.GetValue(obj: ((sculptureExample = GenSpawn.Spawn(newThing: ThingMaker.MakeThing(def: sculpture, stuff: thing.Stuff), loc: new IntVec3(newX: expanded.minX, newY: 0, newZ: expanded.maxZ - 1), map: map)).Graphic)))[sculptureExample.thingIDNumber % graphics.Length] = graphic;
                    (graphics = (Graphic[])subGraphicInfo.GetValue(obj: ((sculptureExample = GenSpawn.Spawn(newThing: ThingMaker.MakeThing(def: sculpture, stuff: thing.Stuff), loc: expanded.BottomLeft, map: map)).Graphic)))[sculptureExample.thingIDNumber % graphics.Length] = graphic;
                    (graphics = (Graphic[])subGraphicInfo.GetValue(obj: ((sculptureExample = GenSpawn.Spawn(newThing: ThingMaker.MakeThing(def: sculpture, stuff: thing.Stuff), loc: new IntVec3(newX: expanded.maxX - 1, newY: 0, newZ: expanded.minZ), map: map)).Graphic)))[sculptureExample.thingIDNumber % graphics.Length] = graphic;
                }catch(Exception)
                {
                    goto trysculptures;
                }

                FieldInfo moteCount = typeof(MoteCounter).GetField(name: "moteCount", bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new ArgumentNullException();
                
                occupied.ExpandedBy(dist: 2).Cells.Where(predicate: LocCheck).ToList().ForEach(action: c =>
                {
                    moteCount.SetValue(obj: map.moteCounter, value: 0);

                    Vector3 vc = c.ToVector3();
                    MoteMaker.ThrowMicroSparks(loc: vc, map: map);
                    MoteMaker.ThrowHeatGlow(c: c, map: map, size: 40f);
                    MoteMaker.ThrowFireGlow(c: c, map: map, size: 50f);
                    MoteMaker.ThrowLightningGlow(loc: vc, map: map, size: 60f);
                    MoteMaker.ThrowMetaPuff(loc: vc, map: map);
                });

                map.weatherDecider.DisableRainFor(ticks: 5000);
                thing.SetFactionDirect(newFaction: Faction.OfPlayer);
                return true;
            }
        }
    }
}