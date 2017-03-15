using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Ankh
{
    [StaticConstructorOnStartup]
    sealed class Behaviour_Interpreter : MonoBehaviour
    {
        private string logPath;
        private XDocument configFile;
        private readonly string configPath = "AnkhConfig.xml";
        private string configFullPath;
        private List<string> log;


        private static Dictionary<string, Action<bool, bool>> gods;
        private static List<Action<string, bool>> wrathActions;
        private static List<Action> congratsActions;

        public static int zapCount;
        public static int thermCount;
        private static int instaResearch;
        private static int lastTickTick;

        private List<InstanceVariables> instanceVariables;
        private InstanceVariables instanceVariableHolder;

        static Behaviour_Interpreter()
        {
            GameObject initializer = new GameObject("Ankh_Interpreter");
            _instance = initializer.AddComponent<Behaviour_Interpreter>();
            DontDestroyOnLoad(initializer);
        }

        public string ConfigFullPath => Path.Combine(this.configFullPath, this.configPath);

        public string InstanceVariablePath => Path.Combine(this.configFullPath, "vars.erdelf");

        public static readonly Behaviour_Interpreter _instance;

        private void Awake()
        {
            PrepareDefs();
            InitializeStaticVariables();
        }

        private void InitializeStaticVariables()
        {
            this.configFullPath = Path.GetFullPath(this.configPath);
            this.configFullPath = Path.Combine(Path.Combine(Path.Combine(this.configFullPath.Substring(0, this.configFullPath.LastIndexOf(Path.DirectorySeparatorChar)), "Mods"), "AnkhCommandInterpreter"), "Assemblies");

            {

                if (File.Exists(this.InstanceVariablePath))
                {
                    XmlDocument xmlDocument = new XmlDocument();
                    xmlDocument.Load(this.InstanceVariablePath);
                    string xmlString = xmlDocument.OuterXml;

                    using (StringReader read = new StringReader(xmlString))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(List<InstanceVariables>));
                        using (XmlReader reader = new XmlTextReader(read))
                        {
                            this.instanceVariables = (List<InstanceVariables>)serializer.Deserialize(reader);
                        }
                    }
                }
                else
                    this.instanceVariables = new List<InstanceVariables>();
            }

            AddGods();
            AddCongrats();
            AddDeadWraths();

            if (this.log == null)
                this.log = new List<string>();

            lastTickTick = 0;
            this.configFile = XDocument.Load(this.ConfigFullPath);

            foreach (XElement b in this.configFile.Descendants("settings"))
            {
                foreach (XElement x in b.Descendants())
                {
                    if (x.Attributes()?.First() != null)
                        switch (x.Attributes().First().Value)
                        {
                            case "logPath":
                                this.logPath = x.DescendantNodes().First().ToString().Trim();
                                break;
                            case "zapCount":
                                zapCount = int.Parse(x.DescendantNodes().First().ToString().Trim());
                                break;
                            case "instaResearch":
                                instaResearch = int.Parse(x.DescendantNodes().First().ToString().Trim());
                                break;
                            case "thermCount":
                                thermCount = int.Parse(x.DescendantNodes().First().ToString().Trim());
                                break;
                        }
                }
            }
            UpdateLog();
        }

        private void InitializeVariables()
        {
            try
            {
                InstanceVariables instanceVariable = this.instanceVariables.FirstOrDefault(iv => iv.seed.Equals(Current.Game.World.info.Seed));
                if (instanceVariable.seed != 0)
                    this.instanceVariableHolder = instanceVariable;
                else
                {
                    instanceVariable.seed = Current.Game.World.info.Seed;
                    instanceVariable.moodTracker = new Dictionary<string, float>();
                    instanceVariable.scheduler = new Dictionary<int, List<string[]>>();
                    instanceVariable.deadWraths = new List<string>();

                    this.instanceVariableHolder = instanceVariable;
                    this.instanceVariables.Add(instanceVariable);
                }
            }
            catch (Exception) { }
        }

        private void FixedUpdate()
        {
            try
            {
                ((Action)(() =>
                {
                    if (Current.ProgramState == ProgramState.Playing)
                    {
                        if (Current.Game?.World?.info?.Seed != this.instanceVariableHolder.seed)
                            InitializeVariables();

                        if (Find.TickManager.TicksGame / (GenDate.TicksPerDay) > lastTickTick / (GenDate.TicksPerDay))
                        {
                            List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder();

                            pawns.Where(p => !p.Dead).ToList().ForEach(delegate (Pawn p)
                            {
                                if (!this.instanceVariableHolder.moodTracker.ContainsKey(p.NameStringShort))
                                    this.instanceVariableHolder.moodTracker.Add(p.NameStringShort, 0f);
                                this.instanceVariableHolder.moodTracker[p.NameStringShort] = p.needs.mood.CurInstantLevelPercentage;
                            });

                            pawns.Where(p => p.Dead && !this.instanceVariableHolder.deadWraths.Contains(p.NameStringShort)).ToList().ForEach(p =>
                            {
                                this.instanceVariableHolder.deadWraths.Add(p.NameStringShort);
                                if (Rand.Value > 0.3)
                                {
                                    int scheduledFor = Mathf.RoundToInt(UnityEngine.Random.Range(0.0f, (float)GenDate.DaysPerMonth) * GenDate.TicksPerDay);
                                    AddToScheduler(scheduledFor, "wrathCall", p.NameStringShort, p.gender.ToString());
                                    Log.Message("Scheduled " + p.NameStringShort + "s wrath. Will happen in " + GenDate.TicksToDays(scheduledFor).ToString() + " days");
                                }
                            });
                        }



                        if ((Find.TickManager.TicksGame / (GenDate.TicksPerHour / 8) > lastTickTick / (GenDate.TicksPerHour / 8)))
                            this.instanceVariableHolder.scheduler.Keys.Where(i => i < Find.TickManager.TicksGame).ToList().ForEach(i =>
                            {
                                this.instanceVariableHolder.scheduler[i].ForEach(a =>
                                {
                                    try
                                    {
                                        ExecuteScheduledCommand(a);
                                    }
                                    catch (Exception e)
                                    {
                                        Debug.Log(e.Message + "\n" + e.StackTrace);
                                        AddToScheduler(10, a);
                                    }
                                    finally
                                    {
                                        this.instanceVariableHolder.scheduler.Remove(i);
                                    }
                                });
                            });


                        if ((Find.TickManager.TicksGame / (GenDate.TicksPerMonth) > lastTickTick / (GenDate.TicksPerMonth)) && Find.TickManager.TicksGame > 0)
                            if (Find.ColonistBar.GetColonistsInOrder().Any(p => !p.Dead))
                            {
                                AddToScheduler(5, "survivalReward");
                            }
                        //  Find.TickManager.TicksGame % (GenDate.TicksPerHour / 2) == 0
                        if (Find.TickManager.TicksGame / (GenDate.TicksPerHour / 2) > lastTickTick / (GenDate.TicksPerHour / 2))
                        {
                            List<string> curLog = UpdateLog();
                            if (!this.log.NullOrEmpty())
                                curLog.ForEach(
                                    delegate (string s)
                                    {
                                        string[] split = s.Split(' ');
                                        Log.Message(s);
                                        if (split.Length == 4)
                                        {
                                            bool favor = split[0].ToLower().Equals("favor");
                                            if (int.TryParse(split[2], out int points) && int.TryParse(split[3], out int cost))
                                                if (points >= cost)
                                                    AddToScheduler(favor || Find.TickManager.TicksGame > GenDate.TicksPerDay * 3 ? 1 : GenDate.TicksPerDay * 3 - Find.TickManager.TicksGame, "callTheGods", split[1].ToLower(), favor.ToString(), true.ToString());
                                        }
                                    }
                                );
                        }

                        if (instaResearch > 0)
                            if (Find.ResearchManager.currentProj != null)
                            {
                                Find.ResearchManager.ResearchPerformed(500f / 0.009f, null);
                                instaResearch--;
                                this.configFile.Descendants("settings").Descendants().First((XElement x) => x.Attribute("name").Value == "instaResearch").SetValue("\n\t\t" + instaResearch + "\n\t");
                                this.configFile.Save(this.ConfigFullPath);
                            }

                        lastTickTick = Find.TickManager.TicksGame;
                    }
                }))();

            }
            catch (Exception e) { Debug.Log(e.Message + "\n" + e.StackTrace); }
        }

        private void ExecuteScheduledCommand(params string[] parameters)
        {
            switch (parameters[0])
            {
                case "callTheGods":
                    Action<bool, bool> action;
                    if (gods.TryGetValue(parameters[1], out action))
                        action.Invoke(bool.Parse(parameters[2]), bool.Parse(parameters[3]));
                    break;
                case "survivalReward":
                    Find.LetterStack.ReceiveLetter("survival reward", "You survived a month on this rimworld, the gods are pleased", LetterType.Good);
                    congratsActions.RandomElement().Invoke();
                    break;
                case "wrathCall":
                    CustomIncidentCall.WeatherEvent_WrathBombing.erdelf = parameters[1].EqualsIgnoreCase("erdelf");
                    wrathActions.RandomElement().Invoke(parameters[1], (Gender)Enum.Parse(typeof(Gender), parameters[2], true) == Gender.Male);
                    this.instanceVariableHolder.moodTracker.Remove(parameters[1]);
                    break;
            }
        }

        public int SubtractZap()
        {
            zapCount--;
            this.configFile.Descendants("settings").Descendants().First((XElement x) => x.Attribute("name").Value == "zapCount").SetValue("\n\t\t" + zapCount + "\n\t");
            this.configFile.Save(this.ConfigFullPath);
            return zapCount;
        }

        public int SubtractTherm()
        {
            thermCount--;
            this.configFile.Descendants("settings").Descendants().First((XElement x) => x.Attribute("name").Value == "thermCount").SetValue("\n\t\t" + thermCount + "\n\t");
            this.configFile.Save(this.ConfigFullPath);
            return thermCount;
        }

        public void AddToScheduler(int ticks, params string[] parameters)
        {
            Log.Message("Scheduled for " + GenDate.TicksToDays(ticks) + "days: " + string.Join(" ", parameters));

            ticks += Find.TickManager.TicksGame;
            if (!this.instanceVariableHolder.scheduler.ContainsKey(ticks))
                this.instanceVariableHolder.scheduler.Add(ticks, new List<string[]>());
            this.instanceVariableHolder.scheduler[ticks].Add(parameters);
        }

        private void OnDestroy()
        {
            XmlDocument xmlDocument = new XmlDocument();
            XmlSerializer serializer = new XmlSerializer(typeof(List<InstanceVariables>));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.Serialize(stream, this.instanceVariables);
                stream.Position = 0;
                xmlDocument.Load(stream);
                xmlDocument.Save(this.InstanceVariablePath);
            }
        }

        public List<string> UpdateLog()
        {
            List<string> curLog = ReadFileAndFetchStrings(this.logPath);

            int dels = 0;
            int count = curLog.Count;
            for (int i = 0; i < this.log.Count && i < count; i++)
            {
                if (this.log[i] == curLog[i - dels])
                {
                    curLog.RemoveAt(i - dels);
                    dels++;
                }
            }
            foreach (string s in curLog)
                this.log.Add(s);
            return curLog;
        }

        private void AddCongrats() => congratsActions = new List<Action>
            {
                () =>
                {
                    for (int i = 0; i < 15; i++)
                    {
                        AddToScheduler(250+i*50, "callTheGods", gods.Keys.RandomElement(), true.ToString(), true.ToString());
                    }
                    IncidentDef.Named("MiracleHeal").Worker.TryExecute(null);
                }
            };

        private void AddDeadWraths()
        {
            wrathActions = new List<Action<string, bool>>();
            
            Action<string, bool> raidDelegate = delegate (string name, bool gender)
            {
                if (!this.instanceVariableHolder.moodTracker.ContainsKey(name))
                    this.instanceVariableHolder.moodTracker.Add(name, 75f);

                Map map = Find.AnyPlayerHomeMap;
                IncidentParms parms = new IncidentParms()
                {
                    target = map,
                    points = 3.25f * Mathf.Pow(1.1f, 0.75f * (-(0.4f * this.instanceVariableHolder.moodTracker[name]) + 60f)),
                    faction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.SpacerHostile),
                    raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                    raidArrivalMode = PawnsArriveMode.CenterDrop,
                    raidPodOpenDelay = 50,
                    spawnCenter = map.listerBuildings.ColonistsHaveBuildingWithPowerOn(ThingDefOf.OrbitalTradeBeacon) ? DropCellFinder.TradeDropSpot(map) : RCellFinder.TryFindRandomSpotJustOutsideColony(map.IsPlayerHome ? map.mapPawns.FreeColonists.RandomElement().Position : CellFinder.RandomCell(map), map, out IntVec3 spawnPoint) ? spawnPoint : CellFinder.RandomCell(map),
                    generateFightersOnly = true,
                    forced = true,
                    raidNeverFleeIndividual = true
                };
                IEnumerable<Pawn> pawns = new PawnGroupMaker()
                {
                    kindDef = new PawnGroupKindDef()
                    {
                        workerClass = typeof(CustomIncidentCall.PawnGroupKindWorker_Wrath)
                    },
                }.GeneratePawns(new PawnGroupMakerParms()
                {
                    map = parms.target as Map,
                    faction = parms.faction,
                    points = parms.points,
                    generateFightersOnly = true,
                    generateMeleeOnly = false,
                    raidStrategy = parms.raidStrategy
                }).ToList();

                DropPodUtility.DropThingsNear(parms.spawnCenter, map, pawns.Cast<Thing>(), parms.raidPodOpenDelay, false, true, true);
                Lord lord = LordMaker.MakeNewLord(parms.faction, parms.raidStrategy.Worker.MakeLordJob(parms, map), map, pawns);
                AvoidGridMaker.RegenerateAvoidGridsFor(parms.faction, map);

                SendWrathLetter(name, gender, new GlobalTargetInfo(parms.spawnCenter, (Map)parms.target));
            };

            wrathActions.Add(raidDelegate);
            wrathActions.Add(raidDelegate);
            wrathActions.Add((s, b) =>
            {
                SendWrathLetter(s, b, GlobalTargetInfo.Invalid);
                gods.Values.RandomElement().Invoke(false, true);
            });
            wrathActions.Add((s, b) =>
            {
                SendWrathLetter(s, b, GlobalTargetInfo.Invalid);
                gods.Values.RandomElement().Invoke(false, true);
            });

            wrathActions.Add((s, b) =>
            {
                Find.AnyPlayerHomeMap.mapConditionManager.RegisterCondition(MapConditionMaker.MakeCondition(MapConditionDef.Named("wrathConditionDef"), GenDate.TicksPerDay * 1, 0));
                SendWrathLetter(s, b, GlobalTargetInfo.Invalid);
            });
        }

        private void AddGods() => gods = new Dictionary<string, Action<bool, bool>>
            {
                {
                    "zap",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            zapCount++;
                            this.configFile.Descendants("settings").Descendants().First((XElement x) => x.Attribute("name").Value == "zapCount").SetValue("\n\t\t" + zapCount + "\n\t");
                            this.configFile.Save(this.ConfigFullPath);

                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(ThingDef.Named("ZAPActivator"))).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter("zap's favor",
                                    "The god of lightning shows mercy on your colony. He commands the fire in the sky to obey you for once", LetterType.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
                        }
                        else
                        {
                            List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder().Where((Pawn x) => !x.Dead).ToList();
                            if (pawns.Count > 1)
                                pawns.RemoveAll(x => x.NameStringShort.EqualsIgnoreCase("erdelf"));
                            if (pawns.Count > 1)
                                pawns.RemoveAll(x => x.NameStringShort.EqualsIgnoreCase("Serpenthalis"));



                            Pawn p = pawns.Where(((Pawn pawn) => pawn.Map.roofGrid.RoofAt(pawn.Position) != RoofDefOf.RoofRockThick)).RandomElement();
                            p.Map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(p.Map, p.Position));
                            if (letter)
                                Find.LetterStack.ReceiveLetter("zap's wrath",
                                    "The god of lightning is angry at your colony. He commands the fire in the sky to strike down on " + p.NameStringShort, LetterType.BadUrgent, p);
                        }
                    }
                },
                {
                    "peg",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            Map map = Find.AnyPlayerHomeMap;

                            if (!CellFinder.TryFindRandomEdgeCellWith(i => map.reachability.CanReachColony(i), map, out IntVec3 position))
                                throw new Exception();
                            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Rand.Bool ? PawnKindDefOf.SpaceSoldier : DefDatabase<PawnKindDef>.AllDefsListForReading.Where(p => p.RaceProps.Humanlike && !p.factionLeader).RandomElement(), Faction.OfPlayer, PawnGenerationContext.NonPlayer, map, true, false, false, false, true, false, 26f, true, true, true, p => !p.story.traits.HasTrait(TraitDef.Named("Prosthophile"))));

                            GenSpawn.Spawn(pawn, position, map);

                            DefDatabase<SkillDef>.AllDefsListForReading.ForEach(sd => pawn.skills.GetSkill(sd).Level=Rand.Range(15,20));

                            List<RecipeDef> source = DefDatabase<RecipeDef>.AllDefsListForReading.Where(rd =>
                            rd.fixedIngredientFilter.AllowedThingDefs.Any(td => td.isBodyPartOrImplant) && rd.addsHediff != null && rd.addsHediff.addedPartProps != null
                            && rd.addsHediff.addedPartProps.partEfficiency >= 1).ToList();
                            
                            if (source.Any())
                            {
                                source.RemoveRange(0, Math.Max(source.Count / 6 * 1, 5));

                                source.ForEach(recipeDef =>
                                {
                                    if (Rand.Value > 0.6)
                                    {
                                        if (recipeDef.Worker.GetPartsToApplyOn(pawn, recipeDef).Any<BodyPartRecord>())
                                        {
                                            recipeDef.Worker.ApplyOnPawn(pawn, recipeDef.Worker.GetPartsToApplyOn(pawn, recipeDef).RandomElement<BodyPartRecord>(), null, (List<Thing>)typeof(PawnTechHediffsGenerator).GetField("emptyIngredientsList", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
                                        }
                                    }
                                });
                            }
                            if (letter)
                                Find.LetterStack.ReceiveLetter("peg's favor",
                                    "The god of pirates shows mercy on your colony. A hapless pirate was kicked from Peg's flagship and fell near the colony", LetterType.Good, pawn);
                        }
                        else
                        {
                            IncidentParms incidentParms = new IncidentParms(){
                            faction = Find.World.factionManager.AllFactions.ToList().Where((Faction f) => f?.def.Equals(FactionDefOf.Pirate) ?? false)?.RandomElement(),
                                target = Find.AnyPlayerHomeMap
                            };

                            Letter letterobj = new Letter("peg's wrath",
                                    "The god of pirates is angry at your colony. She commands the pirates of this world to attack", LetterType.BadUrgent);

                            CustomIncidentCall.CallEnemyRaid(incidentParms, letter ? letterobj : null);

                        }
                    }
                },
                {
                    "therm",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            thermCount++;
                            this.configFile.Descendants("settings").Descendants().First((XElement x) => x.Attribute("name").Value == "thermCount").SetValue("\n\t\t" + zapCount + "\n\t");
                            this.configFile.Save(this.ConfigFullPath);

                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(ThingDef.Named("ZAPActivator"))).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter("therm's favor",
                                    "The god of fire shows mercy on your colony. He commands the fires of this little world to follow your orders.", LetterType.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));

                            /*
                            Thing geyser = ThingMaker.MakeThing(ThingDefOf.SteamGeyser);
                            IntVec3 intVec;
                            Map map = Find.AnyPlayerHomeMap;

                            Predicate<IntVec3> validator = delegate (IntVec3 c)
                            {
                                if (c.Fogged(map))
                                {
                                    return false;
                                }
                                foreach (IntVec3 current in GenAdj.CellsOccupiedBy(c, Rot4.North, geyser.def.Size))
                                {
                                    if (!current.Standable(map) && current.GetFirstThing(map, ThingDefOf.SteamGeyser) != null)
                                        return false;
                                }
                                return map.reachability.CanReachColony(c);
                            };

                            while (!RCellFinder.TryFindRandomSpotJustOutsideColony(map.areaManager.Home.ActiveCells.RandomElement(), map, map.mapPawns.AllPawns.RandomElement(), out intVec, validator)) { };

                            GenSpawn.Spawn(geyser, intVec, map);

                            foreach (IntVec3 current in GenAdj.CellsOccupiedBy(geyser))
                            {
                                foreach (Thing current2 in map.thingGrid.ThingsAt(current))
                                {
                                    if (current2 is Plant || current2 is Filth)
                                    {
                                        current2.Destroy(DestroyMode.Vanish);
                                    }
                                }
                            }

                            if (letter)
                                Find.LetterStack.ReceiveLetter("therms's favor",
                                    "The god therm shows mercy on your colony. He commands the planet to unearth a geothermal vent near the colony", LetterType.Good, geyser);
                            */
                        }
                        else
                        {
                            List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder().Where((Pawn x) => !x.Dead).ToList();
                            if (pawns.Count > 1)
                                pawns.RemoveAll(x => x.NameStringShort.EqualsIgnoreCase("erdelf"));
                            if (pawns.Count > 1)
                                pawns.RemoveAll(x => x.NameStringShort.EqualsIgnoreCase("Serpenthalis"));

                            Pawn p = pawns.RandomElement();
                            if (letter)
                                Find.LetterStack.ReceiveLetter("therms's wrath",
                                    "The god therm is angry at your colony. He commands the body of " + p.NameStringShort + " to combust", LetterType.BadUrgent, p);
                            foreach (IntVec3 intVec in GenAdjFast.AdjacentCells8Way(p.Position, p.Rotation, p.RotatedSize))
                                if (Rand.Bool)
                                    GenExplosion.DoExplosion(intVec, p.Map, 2f, DamageDefOf.Flame, null);
                                else
                                    GenExplosion.DoExplosion(intVec, p.Map, 2f, DamageDefOf.Stun, null);
                        }
                    }
                },
                {
                    "rootsy",
                    delegate (bool favor, bool letter)
                     {
                         if (favor)
                         {
                             List<Plant> list = new List<Plant>();
                             foreach (Map map in Find.Maps.Where((Map map) => map.ParentFaction == Faction.OfPlayer))
                                 list.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSource).Where((Thing thing) => thing is Plant && thing.def.plant.growDays <= 16f && (thing as Plant).LifeStage == PlantLifeStage.Growing && thing.Map.zoneManager.ZoneAt(thing.Position) is Zone_Growing && !thing.def.defName.EqualsIgnoreCase(ThingDefOf.PlantGrass.defName)).Cast<Plant>());
                             if (list.Count < 10)
                                 throw new Exception();


                             list.ForEach(plant =>
                             {
                                 plant.Growth = 1f;
                                 plant.Map.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlag.Things);
                                 list.Remove(plant);
                             });
                             if (letter)
                                 Find.LetterStack.ReceiveLetter("rootsy's favor",
                                     "The god of flowers shows mercy on your colony. He commands the roots under your colony to grow 30 of your plants to maturation", LetterType.Good);
                         }
                         else
                         {
                             Letter letterobj = new Letter("rootsy's wrath",
                                     "The god of flowers is angry at your colony. He commands the roots under your colony to blight", LetterType.BadNonUrgent);

                             CustomIncidentCall.CallBlight(letter ? letterobj : null);
                         }
                     }
                },
                {
                    "moo",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            List<Pawn> capacity = Find.AnyPlayerHomeMap.mapPawns.AllPawnsSpawned.Where((Pawn x) =>
                               x.RaceProps.Animal && x.Faction == null && !x.Position.Fogged(x.Map) && !x.InMentalState && !x.Downed).ToList();
                            float value = Rand.Range(1000, 1000 * Find.ColonistBar.GetColonistsInOrder().Count);
                            Pawn pawn = null;
                            while (value > 0f && capacity.TryRandomElementByWeight((Pawn x) => x.kindDef.combatPower, out pawn))
                            {
                                if (pawn.guest != null)
                                    pawn.guest.SetGuestStatus(null, false);
                                //                        pawn.mindState.mentalStateHandler.cle
                                pawn.SetFaction(Faction.OfPlayer);
                                capacity.Remove(pawn);
                                value -= pawn.kindDef.combatPower;
                            }
                            if (value > 0f)
                                while (value > 0)
                                {
                                    Thing thing = PawnGenerator.GeneratePawn(PawnKindDef.Named("Cow"));

                                    Map map = Find.AnyPlayerHomeMap;

                                    if (!CellFinder.TryFindRandomEdgeCellWith(x => x.InBounds(map) && map.reachability.CanReachColony(x), map, out IntVec3 c))
                                        throw new Exception();

                                    GenSpawn.Spawn(thing, c, map);
                                    value -= thing.MarketValue;
                                }
                            if (letter)
                                Find.LetterStack.ReceiveLetter("moo's favor",
                                     "The god of animals shows mercy on your colony. He commands his subordinates to be devoted to your colony", LetterType.Good, pawn);

                        }
                        else
                        {
                            Letter letterobj = new Letter("moo's wrath",
                                    "The god of animals is angry at your colony. He commands his subordinates to teach you a lesson", LetterType.BadUrgent);

                            IncidentParms parms = new IncidentParms(){
                                target = Find.AnyPlayerHomeMap,
                                points = 200f * Mathf.Log(Find.ColonistBar.GetColonistsInOrder().Where(x => !x.Dead && !x.Downed).Count()-1)*2f
                            };
                            CustomIncidentCall.CallManhunter(parms, letter ? letterobj : null);
                        }
                    }
                },
                {
                    "beepboop",
                    delegate (bool favor, bool letter)
                   {
                       if (favor)
                       {
                           instaResearch++;
                           this.configFile.Descendants("settings").Descendants().First((XElement x) => x.Attribute("name").Value == "instaResearch").SetValue("\n\t\t" + instaResearch + "\n\t");
                           this.configFile.Save(this.ConfigFullPath);
                           if (letter)
                               Find.LetterStack.ReceiveLetter("beepboop's favor",
                                    "The god beepboop shows mercy on your colony. The colonists find the missing link to finish their current research.", LetterType.Good);
                       }
                       else
                       {
                           Letter letterobj = new Letter("beepboop's wrath",
                                   "The god beepboop is angry at your colony. He causes a military transporter to crash near your colony", LetterType.BadUrgent);
                           IncidentParms parms = new IncidentParms(){
                            target = Find.AnyPlayerHomeMap
                           };
                           CustomIncidentCall.CallShipPart(letter ? letterobj : null, Rand.Value > 0.2f ? 1 : 2);
                       }
                   }
                },
                {
                    "fnargh",
                    delegate (bool favor, bool letter)
                    {
                         if (favor)
                         {
                             Pawn p = Find.ColonistBar?.GetColonistsInOrder()?.Where((Pawn x) => !x.Dead && !x.Downed && !x.mindState.mentalStateHandler.InMentalState && !x.jobs.curDriver.asleep).RandomElement();
                             if (p != null)
                             {
                                 p.needs.mood.thoughts.memories.TryGainMemoryThought(ThoughtDef.Named("FnarghFavor"));
                                 if (letter)
                                     Find.LetterStack.ReceiveLetter("fnargh's favor",
                                          "The god fnargh shows mercy on your colony. He commands the web of thought to make " + p.NameStringShort + " happy", LetterType.Good, p);
                             }
                             else
                                 throw new Exception();
                         }
                         else
                         {
                             List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder().Where((Pawn x) => !x.Dead && !x.Downed && !x.mindState.mentalStateHandler.InMentalState && !x.jobs.curDriver.asleep).ToList();
                             if (pawns.Count > 1)
                                 pawns.RemoveAll(x => x.NameStringShort.EqualsIgnoreCase("erdelf"));
                             if (pawns.Count > 1)
                                 pawns.RemoveAll(x => x.NameStringShort.EqualsIgnoreCase("Serpenthalis"));
                             Pawn p = pawns.RandomElement();
                             if (p != null)
                             {
                                 p.needs.mood.thoughts.memories.TryGainMemoryThought(ThoughtDef.Named("FnarghWrath"));
                                 p.mindState.mentalStateHandler.TryStartMentalState(DefDatabase<MentalStateDef>.AllDefs.Where(msd => !msd.defName.EqualsIgnoreCase("PanicFlee") && !msd.defName.EqualsIgnoreCase("GiveUpExit") && msd.Worker.GetType().GetField("otherPawn") == null).RandomElement(), "Fnargh's wrath", true, true);
                                 if (letter)
                                     Find.LetterStack.ReceiveLetter("fnargh's wrath",
                                         "The god fnargh is angry at your colony. He commands the web of thought to make " + p.NameStringShort + " mad", LetterType.BadNonUrgent, p);
                             }
                             else
                                 throw new Exception();
                         }
                    }
                },
                {
                    "repo",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            MethodInfo missing = typeof(HealthCardUtility).GetMethod("VisibleHediffs", BindingFlags.Static | BindingFlags.NonPublic);
                            List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder().Where((Pawn x) => !x.Dead && ((IEnumerable<Hediff>)missing.Invoke(null, new object[] { x, false })).Where(h => h.GetType() == typeof(Hediff_MissingPart)).Count() > 0).ToList();

                            if (!pawns.NullOrEmpty())
                            {
                                Pawn p = pawns.RandomElement();
                                BodyPartRecord part = ((IEnumerable<Hediff>)missing.Invoke(null, new object[] { p, false })).Where(h => h.GetType() == typeof(Hediff_MissingPart)).RandomElement().Part;
                                p.health.RestorePart(part);

                                if (letter)
                                    Find.LetterStack.ReceiveLetter("repo's favor",
                                        "The god of organs shows mercy on your colony. He restores the missing " + part.def.LabelCap.ToLower() + " of " + p.NameStringShort, LetterType.Good, new GlobalTargetInfo(p.Position, map));
                            }
                            else
                            {
                                IntVec3 intVec = DropCellFinder.TradeDropSpot(map);
#pragma warning disable IDE0018 // Inlinevariablendeklaration
                                ThingDef organDef;
#pragma warning restore IDE0018 // Inlinevariablendeklaration

                                while (!DefDatabase<ThingDef>.AllDefsListForReading.Where((ThingDef x) =>
                                x.thingCategories != null && x.thingCategories.Any((ThingCategoryDef f) =>
                                ThingCategoryDefOf.BodyParts.ThisAndChildCategoryDefs.Contains(f)) && DefDatabase<RecipeDef>.AllDefsListForReading.Where(rd => rd.IsIngredient(x)).
                                FirstOrDefault()?.addsHediff?.addedPartProps?.partEfficiency >= 1).TryRandomElement(out organDef)) { }
                                
                                TradeUtility.SpawnDropPod(intVec, map, ThingMaker.MakeThing(organDef));
                                TradeUtility.SpawnDropPod(intVec, map, ThingMaker.MakeThing(ThingDefOf.GlitterworldMedicine));
                                if (letter)
                                    Find.LetterStack.ReceiveLetter("repo's favor",
                                        "The god of organs shows mercy on your colony. He sends you a little gift from his personal collection", LetterType.Good, new GlobalTargetInfo(intVec, map));
                            }
                        }
                        else
                        {
                            List<BodyPartRecord> parts = new List<BodyPartRecord>();
                            List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder().Where((Pawn x) => !x.Dead).ToList();
                            if (pawns.Count > 1)
                                pawns.RemoveAll(x => x.NameStringShort.EqualsIgnoreCase("erdelf"));
                            if (pawns.Count > 1)
                                pawns.RemoveAll(x => x.NameStringShort.EqualsIgnoreCase("Serpenthalis"));

                            Predicate<BodyPartRecord> bodyPartRecord = delegate (BodyPartRecord x)
                            {
                                if (!(x.def.dontSuggestAmputation || x.depth == BodyPartDepth.Inside))
                                    return true;
                                return false;
                            };

                            if (pawns.Count <= 0)
                                throw new Exception();

                            Pawn p = null;
                            while (parts.NullOrEmpty())
                            {
                                p = pawns.RandomElement();
                                parts = p.health.hediffSet.GetNotMissingParts().Where((BodyPartRecord x) => bodyPartRecord.Invoke(x) && !bodyPartRecord.Invoke(x.parent)).ToList();
                                pawns.Remove(p);
                            }

                            if (parts.NullOrEmpty())
                                throw new Exception();

                            BodyPartRecord part = parts.RandomElement();

                            p.health.AddHediff(HediffDefOf.MissingBodyPart, part);
                            if (letter)
                                Find.LetterStack.ReceiveLetter("repo's wrath",
                                        "The god of organs is angry at your colony. He commands the " + part.def.LabelCap.ToLower() + " of " + p.NameStringShort + " to damage itself", LetterType.BadUrgent, p);
                        }
                    }
                }
            };

        static void PrepareDefs()
        {
            MethodInfo shortHashGiver = typeof(ShortHashGiver).GetMethod("GiveShortHash", BindingFlags.NonPublic | BindingFlags.Static);

            #region MapConditions
            {
                MapConditionDef wrath1ConditionDef = new MapConditionDef()
                {
                    defName = "wrathConditionDef",
                    conditionClass = typeof(CustomIncidentCall.MapCondition_WrathBombing),
                    label = "wrath of the dead",
                    description = "The gods sent their pawn  down in human form to serve your colony... and you failed him",
                    endMessage = "The gods are satisfied with your pain",
                    preventRain = false,
                    canBePermanent = true
                };
                wrath1ConditionDef.ResolveReferences();
                wrath1ConditionDef.PostLoad();
                DefDatabase<MapConditionDef>.Add(wrath1ConditionDef);
            }
            #endregion
            #region Incidents
            {
                IncidentDef miracleHeal = new IncidentDef()
                {
                    defName = "MiracleHeal",
                    label = "miracle heal",
                    targetType = IncidentTargetType.BaseMap,
                    workerClass = typeof(CustomIncidentCall.MiracleHeal),
                    baseChance = 10
                };
                miracleHeal.ResolveReferences();
                miracleHeal.PostLoad();
                shortHashGiver.Invoke(null, new object[] { miracleHeal });
                DefDatabase<IncidentDef>.Add(miracleHeal);
            }
            /*{
                IncidentDef sacrificeOption = new IncidentDef()
                {
                    defName = "SacrificeForTheGods",
                    label = "sacrifice",
                    targetType = IncidentTargetType.BaseMap,
                    workerClass = typeof(CustomIncidentCall.Sacrifice),
                    baseChance = 10
                };
                sacrificeOption.ResolveReferences();
                sacrificeOption.PostLoad();
                DefDatabase<IncidentDef>.Add(sacrificeOption);
            }*/
            #endregion
            #region Buildings
            {
                ThingDef zap = new ThingDef()
                {
                    defName = "ZAPActivator",
                    thingClass = typeof(Buildings.Building_ZAP),
                    label = "ZAP Activator",
                    description = "This device is little more than an altar to Zap, engraved with his jagged yellow symbol. Use it to invoke Zap's favor.",
                    size = new IntVec2(1, 1),
                    passability = Traversability.PassThroughOnly,
                    category = ThingCategory.Building,
                    selectable = true,
                    designationCategory = DesignationCategoryDefOf.Structure,
                    useHitPoints = false,
                    altitudeLayer = AltitudeLayer.Building,
                    leaveResourcesWhenKilled = true,
                    resourcesFractionWhenDeconstructed = 1,
                    rotatable = false,
                    graphicData = new GraphicData()
                    {
                        texPath = "Activator",
                        graphicClass = typeof(Graphic_Single),
                        shaderType = ShaderType.CutoutComplex,
                        drawSize = new Vector2(1, 1)
                    },
                    statBases = new List<StatModifier>()
                        {
                            new StatModifier()
                            {
                                stat = StatDefOf.MaxHitPoints,
                                value = 500
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.WorkToMake,
                                value = 200
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Flammability,
                                value = 0
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Beauty,
                                value = 4
                            }
                        },
                    costList = new List<ThingCountClass>()
                    {
                        //new ThingCountClass(ThingDefOf.ChunkSlagSteel, 2)
                    },
                    building = new BuildingProperties()
                    {
                        isInert = true,
                        ignoreNeedsPower = true
                    },
                    minifiedDef = ThingDef.Named("MinifiedFurniture")
                };
                zap.blueprintDef = (ThingDef) typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { zap, false, null });
                zap.blueprintDef.ResolveReferences();
                zap.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { zap, true, zap.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                zap.frameDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewFrameDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { zap});
                zap.frameDef.ResolveReferences();
                zap.frameDef.PostLoad();

                zap.ResolveReferences();
                zap.PostLoad();

                shortHashGiver.Invoke(null, new object[] { zap });
                shortHashGiver.Invoke(null, new object[] { minifiedDef });
                shortHashGiver.Invoke(null, new object[] { zap.blueprintDef });
                shortHashGiver.Invoke(null, new object[] { zap.frameDef });

                DefDatabase<ThingDef>.Add(zap);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(zap.blueprintDef);
                DefDatabase<ThingDef>.Add(zap.frameDef);
                zap.designationCategory.ResolveReferences();
                zap.designationCategory.PostLoad();
            }
            {
                ThingDef therm = new ThingDef()
                {
                    defName = "THERMActivator",
                    thingClass = typeof(Buildings.Building_Therm),
                    label = "THERM Activator",
                    description = "This device is little more than an altar to Therm, engraved with his fiery symbol. Use it to invoke therm's favor.",
                    size = new IntVec2(1, 1),
                    passability = Traversability.PassThroughOnly,
                    category = ThingCategory.Building,
                    selectable = true,
                    designationCategory = DesignationCategoryDefOf.Structure,
                    useHitPoints = false,
                    altitudeLayer = AltitudeLayer.Building,
                    leaveResourcesWhenKilled = true,
                    resourcesFractionWhenDeconstructed = 1,
                    rotatable = false,
                    graphicData = new GraphicData()
                    {
                        texPath = "Therm",
                        graphicClass = typeof(Graphic_Single),
                        shaderType = ShaderType.CutoutComplex,
                        drawSize = new Vector2(1, 1)
                    },
                    statBases = new List<StatModifier>()
                        {
                            new StatModifier()
                            {
                                stat = StatDefOf.MaxHitPoints,
                                value = 500
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.WorkToMake,
                                value = 200
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Flammability,
                                value = 0
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Beauty,
                                value = 4
                            }
                        },
                    costList = new List<ThingCountClass>()
                    {
                        //new ThingCountClass(ThingDefOf.ChunkSlagSteel, 2)
                    },
                    building = new BuildingProperties()
                    {
                        isInert = true,
                        ignoreNeedsPower = true
                    },
                    minifiedDef = ThingDef.Named("MinifiedFurniture")
                };
                therm.blueprintDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { therm, false, null });
                therm.blueprintDef.ResolveReferences();
                therm.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { therm, true, therm.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                therm.frameDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewFrameDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { therm });
                therm.frameDef.ResolveReferences();
                therm.frameDef.PostLoad();

                therm.ResolveReferences();
                therm.PostLoad();

                shortHashGiver.Invoke(null, new object[] { therm });
                shortHashGiver.Invoke(null, new object[] { minifiedDef });
                shortHashGiver.Invoke(null, new object[] { therm.blueprintDef });
                shortHashGiver.Invoke(null, new object[] { therm.frameDef });

                DefDatabase<ThingDef>.Add(therm);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(therm.blueprintDef);
                DefDatabase<ThingDef>.Add(therm.frameDef);
                therm.designationCategory.ResolveReferences();
                therm.designationCategory.PostLoad();
            }
            #endregion
            #region Thoughts
            {
                ThoughtDef fnarghWrath = new ThoughtDef()
                {
                    defName = "FnarghWrath",
                    durationDays = 1.0f,
                    stackLimit = 100,
                    stackedEffectMultiplier = 1f,
                    stages = new List<ThoughtStage>()
                    {
                        new ThoughtStage()
                        {
                            label = "Fnargh's wrath",
                            description = "Fnargh's presence in my mind is like a thousand writhing insects.",
                            baseMoodEffect = -10f
                        }
                    }
                };
                fnarghWrath.ResolveReferences();
                fnarghWrath.PostLoad();
                shortHashGiver.Invoke(null, new object[] { fnarghWrath });
                DefDatabase<ThoughtDef>.Add(fnarghWrath);
            }
            {
                ThoughtDef fnarghFavor = new ThoughtDef()
                {
                    defName = "FnarghFavor",
                    durationDays = 1.0f,
                    stackLimit = 100,
                    stackedEffectMultiplier = 1f,
                    stages = new List<ThoughtStage>()
                    {
                        new ThoughtStage()
                        {
                            label = "Fnargh's favor",
                            description = "Fnargh's presence in my mind soothes me. Everything seems just a little bit better.",
                            baseMoodEffect = 10f
                        }
                    }
                };
                fnarghFavor.ResolveReferences();
                fnarghFavor.PostLoad();
                shortHashGiver.Invoke(null, new object[] { fnarghFavor });
                DefDatabase<ThoughtDef>.Add(fnarghFavor);
            }

            #endregion
        }

        private static void SendWrathLetter(string name, bool possessive, GlobalTargetInfo info) => Find.LetterStack.ReceiveLetter("wrath of " + name, name + " died, prepare to meet " + (possessive ? "his" : "her") + " wrath", LetterType.BadUrgent, info);

        private static List<string> ReadFileAndFetchStrings(string file)
        {
            List<string> sb;
            try
            {
                sb = new List<string>();
                using (FileStream fs = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (BufferedStream bs = new BufferedStream(fs))
                    {
                        using (StreamReader sr = new StreamReader(bs))
                        {
                            string str;
                            while ((str = sr.ReadLine()) != null)
                            {
                                sb.Add(str);
                            }
                        }
                    }
                }
                return sb;
            }
            catch (Exception e)
            {
                Debug.Log(e.Message + "\n" + e.StackTrace);
                return new List<string>();
            }
        }

        public void WaitAndExecute(Action action) => StartCoroutine("WaitAndExecuteCoroutine", action);

        public IEnumerator WaitAndExecuteCoroutine(Action action)
        {
            yield return 100;
            action();
        }
    }
}