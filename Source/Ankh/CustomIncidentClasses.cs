using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Ankh
{
    static class CustomIncidentClasses
    {
        static MethodInfo RaidPointInfo = typeof(IncidentWorker_Raid).GetMethod("ResolveRaidPoints", BindingFlags.NonPublic | BindingFlags.Instance);
        static MethodInfo RaidFactionInfo = typeof(IncidentWorker_RaidEnemy).GetMethod("TryResolveRaidFaction", BindingFlags.NonPublic | BindingFlags.Instance);
        static MethodInfo RaidStrategyInfo = typeof(IncidentWorker_RaidEnemy).GetMethod("ResolveRaidStrategy", BindingFlags.NonPublic | BindingFlags.Instance);
        static MethodInfo RaidArriveInfo = typeof(IncidentWorker_Raid).GetMethod("ResolveRaidArriveMode", BindingFlags.NonPublic | BindingFlags.Instance);
        static MethodInfo RaidSpawnInfo = typeof(IncidentWorker_Raid).GetMethod("ResolveRaidSpawnCenter", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void CallEnemyRaid(IncidentParms parms, Letter letter)
        {
            object obj = IncidentDefOf.RaidEnemy.Worker;
            Map map = (Map)parms.target;
            parms.points = Rand.Range(50f, 300f);
            RaidPointInfo.Invoke(obj, new object[] { parms });
            if (!(bool)RaidFactionInfo.Invoke(obj, new object[] { parms }))
            {
                throw new Exception();
            }
            RaidStrategyInfo.Invoke(obj, new object[] { parms });
            RaidArriveInfo.Invoke(obj, new object[] { parms });
            RaidSpawnInfo.Invoke(obj, new object[] { parms });
            IncidentParmsUtility.AdjustPointsForGroupArrivalParams(parms);
            PawnGroupMakerParms defaultPawnGroupMakerParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(parms);
            List<Pawn> list = PawnGroupMakerUtility.GeneratePawns(PawnGroupKindDefOf.Normal, defaultPawnGroupMakerParms, true).ToList<Pawn>();
            if (list.Count == 0)
            {
                throw new Exception();
            }
            TargetInfo target = TargetInfo.Invalid;
            if (parms.raidArrivalMode == PawnsArriveMode.CenterDrop || parms.raidArrivalMode == PawnsArriveMode.EdgeDrop)
            {
                DropPodUtility.DropThingsNear(parms.spawnCenter, map, list.Cast<Thing>(), parms.raidPodOpenDelay, false, true, true);
                target = new TargetInfo(parms.spawnCenter, map, false);
            }
            else
            {
                foreach (Pawn current in list)
                {
                    IntVec3 loc = CellFinder.RandomClosewalkCellNear(parms.spawnCenter, map, 8);
                    GenSpawn.Spawn(current, loc, map);
                    target = current;
                }
            }

            Lord lord = LordMaker.MakeNewLord(parms.faction, parms.raidStrategy.Worker.MakeLordJob(parms, map), map, list);
            AvoidGridMaker.RegenerateAvoidGridsFor(parms.faction, map);

            if (letter != null)
            {
                letter.lookTarget = target;
                Find.LetterStack.ReceiveLetter(letter);
            }

            Find.TickManager.slower.SignalForceNormalSpeedShort();
            Find.StoryWatcher.statsRecord.numRaidsEnemy++;
        }

        public static void CallBlight(Letter letter)
        {
            Map map = Find.AnyPlayerHomeMap;
            List<Thing> list = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
            bool flag = false;

            GlobalTargetInfo target = GlobalTargetInfo.Invalid;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                Plant plant = (Plant)list[i];
                if (map.Biome.CommonalityOfPlant(plant.def) == 0f)
                {
                    if (plant.def.plant.growDays <= 16f)
                    {
                        if (plant.LifeStage == PlantLifeStage.Growing || plant.LifeStage == PlantLifeStage.Mature)
                        {
                            plant.CropBlighted();
                            flag = true;
                            if (Rand.Bool)
                                target = plant;
                        }
                    }
                }
            }
            if (!flag)
            {
                throw new Exception();
            }

            if (letter != null)
            {
                letter.lookTarget = target;
                Find.LetterStack.ReceiveLetter(letter);
            }
        }

        public static void CallManhunter(IncidentParms parms, Letter letter)
        {
            Map map = (Map)parms.target;
            if (!DefDatabase<PawnKindDef>.AllDefsListForReading.Where(p => p.RaceProps.Animal).ToList().TryRandomElement(out PawnKindDef pawnKindDef))
            {
                throw new Exception();
            }
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 root, map, 50f))
            {
                throw new Exception();
            }
            List<Pawn> list = ManhunterPackIncidentUtility.GenerateAnimals(pawnKindDef, map.Tile, parms.points);
            for (int i = 0; i < list.Count; i++)
            {
                Pawn pawn = list[i];
                IntVec3 loc = CellFinder.RandomClosewalkCellNear(root, map, 10);
                GenSpawn.Spawn(pawn, loc, map);
                pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent, null, false, false, null);
                pawn.mindState.exitMapAfterTick = Find.TickManager.TicksGame + Rand.Range(GenDate.TicksPerHour, GenDate.TicksPerDay/2);
            }
            if (letter != null)
            {
                letter.lookTarget = list[0];
                Find.LetterStack.ReceiveLetter(letter);
            }
            Find.TickManager.slower.SignalForceNormalSpeedShort();
        }

        public static void CallShipPart(Letter letter, int count = 1)
        {
            Map map = Find.AnyPlayerHomeMap;
            int num = 0;
            IntVec3 cell = IntVec3.Invalid;

            ThingDef shipPart = DefDatabase<ThingDef>.AllDefsListForReading.Where(x => x.defName.Contains("Crashed") && x.defName.Contains("ShipPart")).RandomElement();

            for (int i = 0; i < count; i++)
            {
                Predicate<IntVec3> validator = delegate (IntVec3 c)
                {
                    if (c.Fogged(map))
                    {
                        return false;
                    }
                    foreach (IntVec3 current in GenAdj.CellsOccupiedBy(c, Rot4.North, shipPart.size))
                    {
                        if (!current.Standable(map))
                        {
                            bool result = false;
                            return result;
                        }
                        if (map.roofGrid.Roofed(current))
                        {
                            bool result = false;
                            return result;
                        }
                    }
                    return map.reachability.CanReachColony(c);
                };
                Pawn pawn = map.mapPawns.FreeColonists.RandomElement();
                if (!CellFinder.TryFindRandomCellNear(CellFinder.RandomCell(map), map, 200, validator, out IntVec3 intVec))
                {
                    if (!CellFinderLoose.TryFindRandomNotEdgeCellWith(14, validator, map, out intVec))
                    {
                        break;
                    }
                }
                GenExplosion.DoExplosion(intVec, map, 3f, DamageDefOf.Flame, null, null, null, null, null, 0f, 1, false, null, 0f, 1);
                Building building_CrashedShipPart = (Building)GenSpawn.Spawn(shipPart, intVec, map);
                building_CrashedShipPart.SetFaction(Faction.OfMechanoids, null);

                IncidentParms incidentParms = new IncidentParms()
                {
                    target = Find.VisibleMap
                };
                StorytellerComp storytellerComp = Find.Storyteller.storytellerComps.First((StorytellerComp x) => x is StorytellerComp_ThreatCycle || x is StorytellerComp_RandomMain);
                incidentParms = storytellerComp.GenerateParms(IncidentCategory.ThreatBig, Find.VisibleMap);

                incidentParms.points *= 0.9f;

                if (incidentParms.points < 300f)
                {
                    incidentParms.points = 300f;
                }

                shipPart.thingClass.GetField("pointsLeft").SetValue(building_CrashedShipPart, incidentParms.points);

                num++;
                cell = intVec;
            }
            if (num > 0)
            {
                if (map == Find.VisibleMap)
                {
                    Find.CameraDriver.shaker.DoShake(1f);
                }

                if (letter != null)
                {
                    letter.lookTarget = new TargetInfo(cell, map, false);
                    Find.LetterStack.ReceiveLetter(letter);
                }

            }
            else
                throw new Exception();
        }

        public class PawnGroupKindWorker_Wrath : PawnGroupKindWorker_Normal
        {

            protected override void GeneratePawns(PawnGroupMakerParms parms, PawnGroupMaker groupMaker, List<Pawn> outPawns, bool errorOnZeroResults = true)
            {
                bool allowFood = parms.raidStrategy == null || parms.raidStrategy.pawnsCanBringFood;
                bool forceIncapDone = false;
                float points = parms.points;
                while (points > 0)
                {
                    Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.SpaceSoldier, parms.faction, PawnGenerationContext.NonPlayer, parms.tile, true, false, false, false, true, true, 1f, false, true, allowFood, false, false, null, null, null, null, null, null));
                    p.InitializeComps();
                    if (parms.forceOneIncap && !forceIncapDone)
                    {
                        p.health.forceIncap = true;
                        p.mindState.canFleeIndividual = false;
                        forceIncapDone = true;
                    }
                    points -= p.kindDef.combatPower;
                    outPawns.Add(p);
                }
            }
        }

        public class MapCondition_WrathBombing : GameCondition
        {
            private static readonly IntRange TicksBetweenStrikes = new IntRange(GenDate.TicksPerHour / 4, GenDate.TicksPerDay / 4);

            private int nextBombardmentStrike;

            public override void GameConditionTick()
            {
                if (Find.TickManager.TicksGame > this.nextBombardmentStrike)
                {
                    this.Map.weatherManager.eventHandler.AddEvent(new WeatherEvent_WrathBombing(this.Map, Faction.OfPlayer));
                    this.nextBombardmentStrike = Find.TickManager.TicksGame + TicksBetweenStrikes.RandomInRange;
                }
            }

            public override void End()
            {
                WeatherDef weatherDef = DefDatabase<WeatherDef>.AllDefsListForReading.Where(wd => wd.rainRate > 0.5f).RandomElement();
                this.Map.weatherManager.TransitionTo(weatherDef);
                typeof(WeatherDecider).GetField("curWeatherDuration", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(this.Map.weatherDecider, weatherDef.durationRange.RandomInRange);
                base.End();
            }
        }

        public class WeatherEvent_WrathBombing : WeatherEvent_LightningFlash
        {
            public static bool erdelf;

            Faction faction;
            public WeatherEvent_WrathBombing(Map map, Faction faction) : base(map) => this.faction = faction;

            public override void FireEvent()
            {
                base.FireEvent();
                IntVec3 strikeLoc = IntVec3.Invalid;

                if (this.faction == null || this.map.mapPawns.SpawnedPawnsInFaction(this.faction).Count <= 0)
                    return;
                while (!CellFinder.TryFindRandomCellNear(this.map.mapPawns.SpawnedPawnsInFaction(this.faction).RandomElement().Position, this.map, 100,
                    ((IntVec3 sq) => sq.Standable(this.map)), out strikeLoc)) { }

                GenExplosion.DoExplosion(strikeLoc, this.map, this.map.roofGrid.RoofAt(strikeLoc) == RoofDefOf.RoofRockThick ? erdelf ? 40f : 20f : erdelf ? 56f : 40f, DamageDefOf.Flame, null,
                    SoundDefOf.PlanetkillerImpact, null, null, null, 0f, 1, true, null, 0f, 1);

                /*
                GenExplosion.DoExplosion(strikeLoc, map, map.roofGrid.RoofAt(strikeLoc) == RoofDefOf.RoofRockThick ? 5f : 10f, DamageDefOf.Flame, null,
                    SoundDefOf.PlanetkillerImpact, null, null, null, 0f, 1, true, null, 0f, 1);
                GenExplosion.DoExplosion(strikeLoc, map, map.roofGrid.RoofAt(strikeLoc) == RoofDefOf.RoofRockThick ? 5f : 10f, DamageDefOf.EMP, null,
                    SoundDefOf.PlanetkillerImpact, null, null, null, 0f, 1, true, null, 0f, 1);
                */

                this.map.roofGrid.SetRoof(strikeLoc, null);
                Vector3 loc = strikeLoc.ToVector3Shifted();
                MoteMaker.ThrowSmoke(loc, this.map, 1.5f);
                MoteMaker.ThrowMicroSparks(loc, this.map);
                MoteMaker.ThrowLightningGlow(loc, this.map, 1.5f);
                MoteMaker.ThrowDustPuff(loc, this.map, 1.5f);
                MoteMaker.ThrowFireGlow(strikeLoc, this.map, 1.5f);
                MoteMaker.ThrowHeatGlow(strikeLoc, this.map, 1.5f);
            }
        }

        public class MiracleHeal : IncidentWorker
        {
            protected override bool CanFireNowSub(IIncidentTarget target) => 
                ((Map)target).mapPawns.FreeColonists.Any((Pawn col) => col.health.hediffSet.GetHediffs<Hediff_Injury>().Count() > 0 && (col.Faction == Faction.OfPlayer));

            public override bool TryExecute(IncidentParms parms)
            {
                List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder();
                if (pawns.Count > 0)
                {
                    while (pawns.Count > 7)
                        pawns.RemoveAt(Rand.Range(0, pawns.Count));
                    pawns.ForEach(pawn =>
                    {
                        foreach (Hediff_Injury current in pawn.health.hediffSet.GetHediffs<Hediff_Injury>())
                            current.Heal((int)current.Severity + 1);
                    });
                    Find.LetterStack.ReceiveLetter("Miracle Heal",
                            "The Gods healed your injuries!", LetterDefOf.Good);
                    return true;
                }
                return false;
            }
        }

        public class AltarAppearance : IncidentWorker
        {
            protected override bool CanFireNowSub(IIncidentTarget target) => 
                !((Map)target).listerThings.ThingsOfDef(AnkhDefOf.sacrificeAltar).Any();

            public override bool TryExecute(IncidentParms parms)
            {
                Map map = parms.target as Map;
                Thing thing;
                IntVec3 loc = map.AllCells.Where(ivc => ivc.Standable(map) && !ivc.Fogged(map) && ivc.GetTerrain(map).affordances.Contains(TerrainAffordance.Heavy) && !ivc.CloseToEdge(map, Mathf.RoundToInt(map.Size.LengthHorizontal/4))).RandomElement();
                Find.LetterStack.ReceiveLetter("Altar appeared", "An altar of the Gods appeared. They might have something to offer.", LetterDefOf.Good, 
                    thing = GenSpawn.Spawn(ThingMaker.MakeThing(AnkhDefOf.sacrificeAltar, GenStuff.RandomStuffFor(AnkhDefOf.sacrificeAltar)), loc, map));
                thing.SetFactionDirect(Faction.OfPlayer);

                CellRect occupied = GenAdj.OccupiedRect(thing);

                Predicate<IntVec3> locCheck = new Predicate<IntVec3>(ivc =>
                {
                    return !occupied.Contains(ivc) && ivc.Standable(map) && !ivc.Fogged(map) && !ivc.GetThingList(map).Any(t => t.Faction == Faction.OfPlayer);
                });

                CellRect.CenteredOn(loc, 10).Cells.Where(ivc => locCheck(ivc)).ToList().ForEach(c => 
                    map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(map, c)));

                occupied.ExpandedBy(2).Cells.Where(ivc => locCheck(ivc)).ToList().ForEach(c =>
                {
                    Vector3 vc = c.ToVector3();
                    MoteMaker.ThrowMicroSparks(vc, map);
                    MoteMaker.ThrowHeatGlow(c, map, 10f);
                    MoteMaker.ThrowFireGlow(c, map, 10f);
                    MoteMaker.ThrowLightningGlow(vc, map, 10f);
                    MoteMaker.ThrowMetaPuff(vc, map);
                    map.terrainGrid.SetTerrain(loc, TerrainDefOf.Concrete);
                });



                if(Find.VisibleMap != map)
                    Current.Game.VisibleMap = map;
                Find.CameraDriver.SetRootPosAndSize(Find.VisibleMap.rememberedCameraPos.rootPos, 11f);
                Find.CameraDriver.JumpToVisibleMapLoc(loc);
                return true;
            }
        }
    }
}