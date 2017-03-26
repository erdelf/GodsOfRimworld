using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public class BehaviourInterpreter : MonoBehaviour
    {
        private string logPath;
        
        private XDocument configFile;
        
        private const string configPath = "AnkhConfig.xml";
        
        private static string configFullPath;

        private List<string> log;

        public static StaticVariables staticVariables;

        private static Dictionary<string, Action<bool, bool>> gods;

        private static List<Action<string, bool>> wrathActions;

        private static List<Action> congratsActions;

        private InstanceVariables instanceVariableHolder;

        static BehaviourInterpreter()
        {
            GameObject initializer = new GameObject("Ankh_Interpreter");

            configFullPath = Path.GetFullPath(configPath);
            configFullPath = Path.Combine(Path.Combine(Path.Combine(configFullPath.Substring(0, configFullPath.LastIndexOf(Path.DirectorySeparatorChar)), "Mods"), "AnkhCommandInterpreter"), "Assemblies");
            
            {
                if (File.Exists(InstanceVariablePath))
                {
                    XmlDocument xmlDocument = new XmlDocument();
                    xmlDocument.Load(InstanceVariablePath);
                    string xmlString = xmlDocument.OuterXml;

                    using (StringReader read = new StringReader(xmlString))
                    {
                        XmlSerializer instanceSerializer = new XmlSerializer(typeof(StaticVariables));

                        using (XmlReader reader = new XmlTextReader(read))
                        {
                            staticVariables = (StaticVariables)instanceSerializer.Deserialize(reader);
                        }
                    }
                }
            }
            _instance = initializer.AddComponent<BehaviourInterpreter>();
            
            DontDestroyOnLoad(initializer);
        }

        public static string ConfigFullPath => Path.Combine(configFullPath, configPath);

        public static string InstanceVariablePath => Path.Combine(configFullPath, "vars.erdelf");

        public static BehaviourInterpreter _instance;

        private void Awake()
        {
            PrepareDefs();
            InitializeStaticVariables();
        }

        private void InitializeStaticVariables()
        {
            AddGods();
            AddCongrats();
            AddDeadWraths();

            if (this.log == null)
                this.log = new List<string>();


            this.configFile = XDocument.Load(ConfigFullPath);

            

            if(staticVariables.instanceVariables.NullOrEmpty())
                staticVariables.instanceVariables = new List<InstanceVariables>();

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
                        }
                }
            }
            UpdateLog();
        }

        private void InitializeVariables()
        {
            try
            {
                InstanceVariables instanceVariable = staticVariables.instanceVariables.FirstOrDefault(iv => iv.seed.Equals(Current.Game.World.info.Seed));
                if (instanceVariable.seed != 0)
                    this.instanceVariableHolder = instanceVariable;
                else
                {
                    instanceVariable.seed = Current.Game.World.info.Seed;
                    instanceVariable.moodTracker = new Dictionary<string, float>();
                    instanceVariable.scheduler = new Dictionary<int, List<string[]>>();
                    instanceVariable.deadWraths = new List<string>();

                    this.instanceVariableHolder = instanceVariable;
                    staticVariables.instanceVariables.Add(instanceVariable);
                }
            }
            catch (Exception ex) { Debug.Log(ex.Message + "\n" + ex.StackTrace); }
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

                        if (Find.TickManager.TicksGame / (GenDate.TicksPerDay) > this.instanceVariableHolder.lastTickTick / (GenDate.TicksPerDay))
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
                                    int scheduledFor = Mathf.RoundToInt(UnityEngine.Random.Range(0.0f, (float)GenDate.DaysPerSeason) * GenDate.TicksPerDay);
                                    AddToScheduler(scheduledFor, "wrathCall", p.NameStringShort, p.gender.ToString());
                                    Log.Message("Scheduled " + p.NameStringShort + "s wrath. Will happen in " + GenDate.TicksToDays(scheduledFor).ToString() + " days");
                                }
                            });
                        }


                        if ((Find.TickManager.TicksGame / (GenDate.TicksPerHour / 8) > this.instanceVariableHolder.lastTickTick / (GenDate.TicksPerHour / 8)))
                        {
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
                                Find.ColonistBar.GetColonistsInOrder().ForEach(p =>
                                {
                                    if (p.story.traits.HasTrait(AnkhDefs.coneOfShame) && !p.health.hediffSet.HasHediff(AnkhDefs.coneOfShameHediff))
                                        p.health.hediffSet.AddDirect(HediffMaker.MakeHediff(AnkhDefs.coneOfShameHediff, p));
                                    if (p.story.traits.HasTrait(AnkhDefs.fiveKnuckleShuffle) && !p.health.hediffSet.HasHediff(AnkhDefs.fiveKnuckleShuffleHediff))
                                        p.health.hediffSet.AddDirect(HediffMaker.MakeHediff(AnkhDefs.fiveKnuckleShuffleHediff, p));
                                });
                            });
                        }
                        if ((Find.TickManager.TicksGame / (GenDate.TicksPerMonth) > this.instanceVariableHolder.lastTickTick / (GenDate.TicksPerMonth)) && Find.TickManager.TicksGame > 0)
                            if (Find.ColonistBar.GetColonistsInOrder().Any(p => !p.Dead))
                            {
                                AddToScheduler(5, "survivalReward");
                            }
                        //  Find.TickManager.TicksGame % (GenDate.TicksPerHour / 2) == 0

                        if (Find.TickManager.TicksGame / (GenDate.TicksPerHour / 2) > this.instanceVariableHolder.lastTickTick / (GenDate.TicksPerHour / 2))
                        {
                            if(Rand.Bool)
                            {
                                Find.ColonistBar.GetColonistsInOrder().Where(p => !p.Dead && p.story.traits.HasTrait(AnkhDefs.teaAndScones)).ToList().ForEach(p =>
                                {
                                    int i = 2;

                                    List<Hediff_Injury> hediffs = p.health.hediffSet.GetHediffs<Hediff_Injury>().Where(hi => !hi.IsOld()).ToList();
                                    while (i > 0 && hediffs.Count > 0)
                                    {
                                        Hediff_Injury hediff = hediffs.First();
                                        i -= Mathf.RoundToInt(hediff.Severity);
                                        hediff.Heal(hediff.Severity + 1);
                                        hediffs.Remove(hediff);
                                    }
                                });
                            }

                            List<string> curLog = UpdateLog();
                            if (!this.log.NullOrEmpty())
                                curLog.ForEach(
                                    delegate (string s)
                                    {
                                        string[] split = s.Split(' ');
                                        Log.Message(s);
                                        if (split.Length == 5)
                                        {
                                            bool favor = split[1].ToLower().Equals("favor");
                                            if (int.TryParse(split[3], out int points) && int.TryParse(split[4], out int cost))
                                                if (points >= cost || split[0].EqualsIgnoreCase("itspladd") || split[0].EqualsIgnoreCase("erdelf") || (split[0].EqualsIgnoreCase("serphentalis") && points >= cost/2 ))
                                                    AddToScheduler(favor || Find.TickManager.TicksGame > GenDate.TicksPerDay * 3 ? 1 : GenDate.TicksPerDay * 3 - Find.TickManager.TicksGame, "callTheGods", split[2].ToLower(), favor.ToString(), true.ToString());
                                        }
                                    }
                                );
                        }
                        
                        if (staticVariables.instaResearch > 0)
                            if (Find.ResearchManager.currentProj != null)
                            {
                                Find.ResearchManager.ResearchPerformed(400f / 0.009f, null);
                                staticVariables.instaResearch--;
                            }
                        this.instanceVariableHolder.lastTickTick = Find.TickManager.TicksGame;
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
                    if (gods.TryGetValue(parameters[1], out Action<bool, bool> action))
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

        public void AddToScheduler(int ticks, params string[] parameters)
        {
            Log.Message("Scheduled in " + GenDate.TicksToDays(ticks) + " days: " + string.Join(" ", parameters));

            ticks += Find.TickManager.TicksGame;
            if (!this.instanceVariableHolder.scheduler.ContainsKey(ticks))
                this.instanceVariableHolder.scheduler.Add(ticks, new List<string[]>());
            this.instanceVariableHolder.scheduler[ticks].Add(parameters);
        }

        private void OnDestroy()
        {
            XmlDocument xmlDocument = new XmlDocument();
            XmlSerializer serializer = new XmlSerializer(typeof(StaticVariables));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.Serialize(stream, staticVariables);
                stream.Position = 0;
                xmlDocument.Load(stream);
                xmlDocument.Save(InstanceVariablePath);
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
                    AnkhDefs.miracleHeal.Worker.TryExecute(null);
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
                Find.AnyPlayerHomeMap.mapConditionManager.RegisterCondition(MapConditionMaker.MakeCondition(AnkhDefs.wrathCondition, GenDate.TicksPerDay * 1, 0));
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
                            staticVariables.zapCount++;

                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(AnkhDefs.zapActivator)).ToList();

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
                    "sparto",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            
                            if (!map.areaManager.Home.ActiveCells.Where(i => i.Standable(map)).TryRandomElement(out IntVec3 position))
                                throw new Exception("no home cell");

                            if(!DefDatabase<ThingDef>.AllDefsListForReading.Where(def => def.equipmentType == EquipmentType.Primary && !(def.weaponTags?.TrueForAll(s => s.Contains("Mechanoid") || s.Contains("Turret") || s.Contains("Artillery")) ?? false)).ToList().TryRandomElement(out ThingDef tDef))
                                throw new Exception("no weapon");

                            Thing thing = ThingMaker.MakeThing(tDef, tDef.MadeFromStuff ? GenStuff.RandomStuffFor(tDef) : null);
                            CompQuality qual = thing.TryGetComp<CompQuality>();
                            if(qual != null)
                                qual.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);

                            GenSpawn.Spawn(thing, position, map);
                            Vector3 vec = position.ToVector3();
                            MoteMaker.ThrowSmoke(vec, map, 5);
                            MoteMaker.ThrowMetaPuff(vec, map);

                            if (letter)
                                Find.LetterStack.ReceiveLetter("sparto's favor",
                                    "The god of war shows mercy on your colony. A piece of equipment below his godly standards got thrown on your colony", LetterType.Good, thing);
                        }
                        else
                        {
                            IncidentParms incidentParms = new IncidentParms(){
                                target = Find.AnyPlayerHomeMap
                            };

                            Letter letterobj = new Letter("sparto's wrath",
                                    "The god of war is angry at your colony. She commands the locals of this world to attack", LetterType.BadUrgent);

                            CustomIncidentCall.CallEnemyRaid(incidentParms, letter ? letterobj : null);

                        }
                    }
                },
                {
                    "peg",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            staticVariables.pegCount++;

                            ThingDef peg = AnkhDefs.pegActivator;
                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(peg)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter("peg's favor",
                                    "The god of pirates shows mercy on your colony. He commands the fires of this world to defend you", LetterType.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
                        }
                        else
                        {
                            for(int i=0; i<3; i++)
                            {
                                Map map = Find.AnyPlayerHomeMap;
                                if (!map.areaManager.Home.ActiveCells.Where(iv => iv.Standable(map)).TryRandomElement( out IntVec3 position))
                                    throw new Exception();
                                GenExplosion.DoExplosion(position, map, 3.9f, DamageDefOf.Bomb, null, null, null, null, null, 0f, 1, false, null, 0f, 1);
                            }
                            if (letter)
                                Find.LetterStack.ReceiveLetter("peg's wrath",
                                    "The god of pirates is angry at your colony. He commands the fires to strike down on your colony", LetterType.BadUrgent);
                        }
                    }
                },
                {
                    "repo",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            staticVariables.repoCount++;
                            ThingDef peg = AnkhDefs.repoActivator;
                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(peg)).ToList();
                            if (letter)
                                Find.LetterStack.ReceiveLetter("repo's favor",
                                    "The god of organs shows mercy on your colony", LetterType.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
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
                },
                {
                    "bob",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {
                            staticVariables.bobCount++;
                            ThingDef peg = AnkhDefs.bobActivator;
                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(peg)).ToList();
                            if (letter)
                                Find.LetterStack.ReceiveLetter("bob's favor",
                                    "The god of buildings shows mercy on your colony", LetterType.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
                        }else
                        {
                            List<Building> walls = Find.AnyPlayerHomeMap.listerBuildings.AllBuildingsColonistOfDef(ThingDefOf.Wall).ToList();

                            if(walls == null || walls.Count() < 2)
                                throw new Exception();

                            Building wall = null;
                            for(int i = 0; i<2; i++)
                            {
                                if(walls.TryRandomElement(out wall))
                                {
                                    wall.Destroy();
                                    walls.Remove(wall);
                                }
                            }
                            if (letter)
                                Find.LetterStack.ReceiveLetter("bob's wrath",
                                        "The god of buildings is angry at your colony.", LetterType.BadUrgent, wall);
                        }
                    }
                },
                {
                    "rootsy",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {
                            staticVariables.rootsyCount++;
                            ThingDef peg = AnkhDefs.rootsyActivator;
                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(peg)).ToList();
                            if (letter)
                                Find.LetterStack.ReceiveLetter("rootsy's favor",
                                    "The god of plants shows mercy on your colony", LetterType.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
                        } else
                        {
                            List<Plant> list = new List<Plant>();
                            foreach (Map map in Find.Maps.Where((Map map) => map.ParentFaction == Faction.OfPlayer))
                                list.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSource).Where((Thing thing) => thing is Plant && thing.def.plant.growDays <= 16f && (thing as Plant).LifeStage == PlantLifeStage.Growing && thing.Map.zoneManager.ZoneAt(thing.Position) is Zone_Growing && !thing.def.defName.EqualsIgnoreCase(ThingDefOf.PlantGrass.defName)).Cast<Plant>());
                            if (list.Count < 10)
                                throw new Exception();
                            list = list.InRandomOrder().Take(30).ToList();

                            list.ForEach(plant =>
                            {
                                plant.CropBlighted();
                                list.Remove(plant);
                            });

                            if(letter)
                                Find.LetterStack.ReceiveLetter("rootsy's wrath",
                                     "The god of flowers is angry at your colony. He commands the roots under your colony to blight", LetterType.BadNonUrgent);

                        }
                    }
                },
                {
                    "fondle",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {
                            Map map = Find.AnyPlayerHomeMap;

                            if (!map.areaManager.Home.ActiveCells.Where(i => i.Standable(map)).TryRandomElement(out IntVec3 position))
                                throw new Exception();

                            DefDatabase<ThingDef>.AllDefsListForReading.Where(td => td.thingClass == typeof(Building_Art)).TryRandomElement(out ThingDef tDef);

                            Thing thing = ThingMaker.MakeThing(tDef, tDef.MadeFromStuff ? GenStuff.RandomStuffFor(tDef) : null);
                            CompQuality qual = thing.TryGetComp<CompQuality>();
                            if(qual != null)
                                qual.SetQuality(Rand.Bool ?
                                    QualityCategory.Normal : Rand.Bool ?
                                    QualityCategory.Good : Rand.Bool ?
                                    QualityCategory.Superior : Rand.Bool ?
                                    QualityCategory.Excellent : Rand.Bool ?
                                    QualityCategory.Masterwork :
                                    QualityCategory.Legendary, ArtGenerationContext.Colony);

                            GenSpawn.Spawn(thing, position, map);
                            Vector3 vec = position.ToVector3();
                            MoteMaker.ThrowSmoke(vec, map, 5);
                            MoteMaker.ThrowMetaPuff(vec, map);

                            if (letter)
                                Find.LetterStack.ReceiveLetter("fondle's favor",
                                    "The god of art shows mercy on your colony.", LetterType.Good, thing);
                        }else
                        {
                            Map map = Find.AnyPlayerHomeMap;

                            map.listerThings.AllThings.Where(t => t.TryGetComp<CompQuality>()?.Quality > QualityCategory.Awful && (t.Position.GetZone(map) is Zone_Stockpile || t.Faction == Faction.OfPlayer)).TryRandomElement(out Thing thing);

                            if(thing == null)
                                throw new Exception();

                            thing.TryGetComp<CompQuality>().SetQuality(QualityCategory.Awful, ArtGenerationContext.Colony);

                            if(letter)
                                Find.LetterStack.ReceiveLetter("fondle's wrath",
                                     "The god of art is angry at your colony.", LetterType.BadNonUrgent, thing);
                        }
                    }
                },
                {
                    "moo",
                    delegate(bool favor, bool letter)
                    {
                        Map map = Find.AnyPlayerHomeMap;
                        if (!DefDatabase<PawnKindDef>.AllDefsListForReading.Where(p => p.RaceProps.Animal).ToList().TryRandomElement(out PawnKindDef pawnKindDef))
                        {
                            throw new Exception();
                        }
                        if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 root, map))
                        {
                            throw new Exception();
                        }
                        Pawn pawn = PawnGenerator.GeneratePawn(pawnKindDef);

                        IntVec3 loc = CellFinder.RandomClosewalkCellNear(root, map, 10);
                        GenSpawn.Spawn(pawn, loc, map);

                        if(favor)
                        {
                            pawn.SetFaction(Faction.OfPlayer);
                            if (letter)
                                Find.LetterStack.ReceiveLetter("moo's favor",
                                     "The god of animals shows mercy on your colony. He commands his subordinate to be devoted to your colony", LetterType.Good, pawn);

                        } else
                        {
                            pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent, null, true, false, null);
                            if(letter)
                                Find.LetterStack.ReceiveLetter("moo's wrath",
                                    "The god of animals is angry at your colony. He commands his subordinate to teach you a lesson", LetterType.BadUrgent, pawn);

                        }
                    }
                },
                {
                    "clink",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {
                            IncidentDefOf.OrbitalTraderArrival.Worker.TryExecute(new IncidentParms() { target = Find.AnyPlayerHomeMap });
                            if(letter)
                                Find.LetterStack.ReceiveLetter("clink's favor",
                                         "The god of commerce shows mercy on your colony.", LetterType.Good);
                        } else
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            List<Thing> silver = map.listerThings.ThingsOfDef(ThingDefOf.Silver);

                            if(silver.Sum(t => t.stackCount) < 100)
                                throw new Exception();

                            int i = 100;

                            while(i > 0)
                            {
                                Thing piece = silver.First();
                                int x = Math.Min(piece.stackCount, i);
                                i -= x;
                                piece.stackCount -= x;

                                if(piece.stackCount == 0)
                                {
                                    silver.Remove(piece);
                                    piece.Destroy();
                                }
                            }
                            for(int c = 0; c<30; c++)
                            {
                                if (!map.areaManager.Home.ActiveCells.Where(l => l.Standable(map)).TryRandomElement(out IntVec3 position))
                                    throw new Exception();

                                Vector3 vec = position.ToVector3();
                                MoteMaker.ThrowSmoke(vec, map, 5);
                                MoteMaker.ThrowMetaPuff(vec, map);

                                GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Steel), position, map);
                            }
                            if(letter)
                                Find.LetterStack.ReceiveLetter("clink's wrath",
                                    "The god of commerce is angry at your colony.", LetterType.BadNonUrgent);
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
                                 p.needs.mood.thoughts.memories.TryGainMemoryThought(AnkhDefs.fnarghFavor);
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
                                 p.needs.mood.thoughts.memories.TryGainMemoryThought(AnkhDefs.fnarghWrath);
                                 p.mindState.mentalStateHandler.TryStartMentalState(DefDatabase<MentalStateDef>.AllDefs.Where(msd => !msd.defName.EqualsIgnoreCase("PanicFlee") && !msd.defName.EqualsIgnoreCase("GiveUpExit") && msd.Worker.GetType().GetField("otherPawn", (BindingFlags) 60) == null).RandomElement(), "Fnargh's wrath", true, true);
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
                    "therm",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            staticVariables.thermCount++;

                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(AnkhDefs.thermActivator)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter("therm's favor",
                                    "The god of fire shows mercy on your colony. He commands the fires of this little world to follow your orders.", LetterType.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
                        } else
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
                    "beepboop",
                    delegate (bool favor, bool letter)
                    {
                        if(favor)
                        {
                            staticVariables.instaResearch++;
                            if (letter)
                               Find.LetterStack.ReceiveLetter("beepboop's favor",
                                    "The god beepboop shows mercy on your colony. The colonists find the missing link to finish their current research.", LetterType.Good);

                        } else
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            if (!map.areaManager.Home.ActiveCells.Where(iv => iv.Standable(map)).TryRandomElement( out IntVec3 position))
                                throw new Exception();

                            PawnKindDef localKindDef = PawnKindDef.Named("Scyther");
                            Faction faction = FactionUtility.DefaultFactionFrom(localKindDef.defaultFactionType);
                            Pawn newPawn = PawnGenerator.GeneratePawn(localKindDef, faction);

                            GenSpawn.Spawn(newPawn, position, map);

                            if (faction != null && faction != Faction.OfPlayer)
                            {
                                Lord lord = null;
                                if (newPawn.Map.mapPawns.SpawnedPawnsInFaction(faction).Any((Pawn p) => p != newPawn))
                                {
                                    Predicate<Thing> validator = (Thing p) => p != newPawn && ((Pawn)p).GetLord() != null;
                                    Pawn p2 = (Pawn)GenClosest.ClosestThing_Global(newPawn.Position, newPawn.Map.mapPawns.SpawnedPawnsInFaction(faction), 99999f, validator);
                                    lord = p2.GetLord();
                                }
                                if (lord == null)
                                {
                                    LordJob_DefendPoint lordJob = new LordJob_DefendPoint(newPawn.Position);
                                    lord = LordMaker.MakeNewLord(faction, lordJob, Find.VisibleMap, null);
                                }
                                lord.AddPawn(newPawn);
                            }
                            if(position.Roofed(map))
                            {
                                newPawn.DeSpawn();
                                TradeUtility.SpawnDropPod(position, map, newPawn);
                            } else
                            {
                                MoteMaker.ThrowMetaPuffs(CellRect.CenteredOn(position, 10), map);
                            }


                            if (letter)
                                Find.LetterStack.ReceiveLetter("beepboop's wrath",
                                    "The god of robots is angry at your colony.", LetterType.BadUrgent, new GlobalTargetInfo(position, map));
                        }
                    }
                },
                {
                    "humour",
                    delegate (bool favor, bool letter)
                    {
                        if(favor)
                        {
                            staticVariables.humourCount++;

                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(AnkhDefs.humourActivator)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter("humour's favor",
                                    "The god of healing shows mercy on your colony.", LetterType.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
                        } else
                        {
                            Pawn p = Find.ColonistBar?.GetColonistsInOrder()?.Where((Pawn x) => !x.Dead && !x.Downed && !x.mindState.mentalStateHandler.InMentalState && !x.jobs.curDriver.asleep).RandomElement();
                            if (p != null)
                            {
                                p.health.AddHediff(HediffDefOf.WoundInfection, p.health.hediffSet.GetRandomNotMissingPart(DamageDefOf.Bullet));
                                if (letter)
                                    Find.LetterStack.ReceiveLetter("humour's wrath",
                                        "The god humour is angry at your colony.", LetterType.BadUrgent, p);
                            }
                            else
                                throw new Exception();
                        }
                    }
                },
                {
                    "taylor",
                    delegate (bool favor, bool letter)
                    {
                        if(favor)
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            IEnumerable<ThingDef> apparelList = DefDatabase<ThingDef>.AllDefsListForReading.Where(td => td.IsApparel);

                            IntVec3 intVec = DropCellFinder.TradeDropSpot(map);
                            for(int i=0;i<5;i++)
                            {
                                if(apparelList.TryRandomElement(out ThingDef apparelDef))
                                {
                                    Thing apparel = ThingMaker.MakeThing(apparelDef, apparelDef.MadeFromStuff ? GenStuff.RandomStuffFor(apparelDef) : null);
                                    CompQuality qual = apparel.TryGetComp<CompQuality>();
                                    if(qual != null)
                                        qual.SetQuality(Rand.Bool ?
                                            QualityCategory.Normal : Rand.Bool ?
                                            QualityCategory.Good : Rand.Bool ?
                                            QualityCategory.Superior : Rand.Bool ?
                                            QualityCategory.Excellent : Rand.Bool ?
                                            QualityCategory.Masterwork :
                                            QualityCategory.Legendary, ArtGenerationContext.Colony);
                                    TradeUtility.SpawnDropPod(intVec, map, apparel);
                                }
                            }
                            if (letter)
                                Find.LetterStack.ReceiveLetter("taylor's favor",
                                    "The god of clothing shows mercy on your colony.", LetterType.Good, new GlobalTargetInfo(intVec, map));
                        } else
                        {
                            List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder().Where((Pawn x) => !x.Dead).ToList();
                            if (pawns.Count > 1)
                                pawns.RemoveAll(x => x.NameStringShort.EqualsIgnoreCase("erdelf"));
                            if (pawns.Count > 1)
                                pawns.RemoveAll(x => x.NameStringShort.EqualsIgnoreCase("Serpenthalis"));



                            Pawn p = pawns.Where(pawn => !pawn.apparel.PsychologicallyNude).RandomElement();
                            p.apparel.DestroyAll();

                            if (letter)
                                Find.LetterStack.ReceiveLetter("taylor's wrath",
                                    "The god of clothing is angry at your colony. He commands the clothing on " + p.NameStringShort + " to destroy itself", LetterType.BadUrgent, p);
                        }
                    }
                },
                {
                    "dorf",
                    delegate (bool favor, bool letter)
                    {
                        if(favor)
                        {
                            staticVariables.dorfCount++;

                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(AnkhDefs.dorfActivator)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter("dorf's favor",
                                    "The god of ming shows mercy on your colony.", LetterType.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
                        } else
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            if (!map.areaManager.Home.ActiveCells.Where(iv => iv.Standable(map)).TryRandomElement( out IntVec3 position))
                                throw new Exception();

                            CellRect cellRect = CellRect.CenteredOn(position, 2);
                            cellRect.ClipInsideMap(Find.VisibleMap);
                            ThingDef granite = ThingDefOf.Granite;
                            foreach (IntVec3 current in cellRect)
                            {
                                GenSpawn.Spawn(granite, current, Find.VisibleMap);
                            }

                            if (letter)
                                Find.LetterStack.ReceiveLetter("dorf's wrath",
                                    "The god of mining is angry at your colony.", LetterType.BadUrgent, new GlobalTargetInfo(position, map));
                        }
                    }
                },
                {
                    "downward_dick",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {
                            Find.ColonistBar.GetColonistsInOrder().Where((Pawn x) => !x.Dead).ToList().ForEach(p =>
                            {
                                Trait trait = null;
                                do
                                {
                                    trait = new Trait(AnkhDefs.ankhTraits.RandomElement(), 0, true);
                                    p.story.traits.GainTrait(trait);
                                }while (!p.story.traits.allTraits.Contains(trait));
                                p.story.traits.allTraits.Remove(trait);
                                p.story.traits.allTraits.Insert(0, trait);
                            });
                            if (letter)
                                Find.LetterStack.ReceiveLetter("dick's favor",
                                    "The god of dicks shows mercy on your colony.", LetterType.Good);
                        } else
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            List<Pawn> colonists = Find.ColonistBar.GetColonistsInOrder().Where((Pawn x) => !x.Dead).ToList();

                            IncidentParms parms = new IncidentParms()
                            {
                                target = map,
                                points = colonists.Count * PawnKindDefOf.SpaceSoldier.combatPower,
                                faction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.SpacerHostile),
                                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                                raidArrivalMode = PawnsArriveMode.CenterDrop,
                                raidPodOpenDelay = GenDate.TicksPerHour/2,
                                spawnCenter = map.listerBuildings.ColonistsHaveBuildingWithPowerOn(ThingDefOf.OrbitalTradeBeacon) ? DropCellFinder.TradeDropSpot(map) : RCellFinder.TryFindRandomSpotJustOutsideColony(map.IsPlayerHome ? map.mapPawns.FreeColonists.RandomElement().Position : CellFinder.RandomCell(map), map, out IntVec3 spawnPoint) ? spawnPoint : CellFinder.RandomCell(map),
                                generateFightersOnly = true,
                                forced = true,
                                raidNeverFleeIndividual = true
                            };
                            List<Pawn> pawns = new PawnGroupMaker()
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

                            IEnumerable<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading.Where(rd => (rd.addsHediff?.addedPartProps?.isBionic ?? false) && 
                            (rd?.fixedIngredientFilter?.AllowedThingDefs.Any(td => td.techHediffsTags?.Contains("Advanced") ?? false) ?? false) && !rd.appliedOnFixedBodyParts.NullOrEmpty());

                            for(int i = 0; i<pawns.Count;i++)
                            {
                                Pawn colonist = colonists[i];
                                Pawn pawn = pawns[i];

                                pawn.Name = colonist.Name;
                                pawn.story.traits.allTraits = colonist.story.traits.allTraits;
                                pawn.story.childhood = colonist.story.childhood;
                                pawn.story.adulthood = colonist.story.adulthood;
                                pawn.skills.skills = colonist.skills.skills;
                                pawn.health.hediffSet.hediffs = colonist.health.hediffSet.hediffs.ListFullCopy();
                                pawn.story.bodyType = colonist.story.bodyType;
                                typeof(Pawn_StoryTracker).GetField("headGraphicPath", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pawn.story, colonist.story.HeadGraphicPath);
                                FieldInfo recordInfo = typeof(Pawn_RecordsTracker).GetField("records", BindingFlags.NonPublic | BindingFlags.Instance);
                                recordInfo.SetValue(pawn.records, recordInfo.GetValue(colonist.records));
                                pawn.gender = colonist.gender;
                                pawn.story.hairDef = colonist.story.hairDef;
                                pawn.story.hairColor = colonist.story.hairColor;
                                pawn.apparel.DestroyAll();

                                colonist.apparel.WornApparel.ForEach(ap =>
                                {
                                    Apparel copy = ThingMaker.MakeThing(ap.def, ap.Stuff) as Apparel;
                                    copy.TryGetComp<CompQuality>().SetQuality(ap.TryGetComp<CompQuality>().Quality, ArtGenerationContext.Colony);
                                    pawn.apparel.Wear(copy);
                                });
                                
                                foreach(FieldInfo fi in typeof(Pawn_AgeTracker).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                                    if(!fi.Name.EqualsIgnoreCase("pawn"))
                                        fi.SetValue(pawn.ageTracker, fi.GetValue(colonist.ageTracker));

                                pawn.story.melanin = colonist.story.melanin;

                                RecipeDef recipe = null;
                                for(int x = 0; x<5; x++)
                                {
                                    recipes.Where(rd => rd != recipe).TryRandomElement(out recipe);
                                    BodyPartRecord record;
                                    do
                                    {
                                        record = pawn.health.hediffSet.GetRandomNotMissingPart(DamageDefOf.Bullet);
                                    } while(recipe.appliedOnFixedBodyParts?.Contains(record.def) ?? false);
                                    recipe.Worker.ApplyOnPawn(pawn, record, null, recipe.fixedIngredientFilter.AllowedThingDefs.Select(td => ThingMaker.MakeThing(td, td.MadeFromStuff ? GenStuff.DefaultStuffFor(td) : null)).ToList());
                                }
                                pawn.equipment.DestroyAllEquipment();
                                ThingDef weaponDef = new ThingDef[] { ThingDef.Named("Gun_AssaultRifle"), ThingDef.Named("Gun_ChargeRifle"), ThingDef.Named("MeleeWeapon_LongSword") }.RandomElement();
                                ThingWithComps weapon = ThingMaker.MakeThing(weaponDef, weaponDef.MadeFromStuff ? ThingDefOf.Plasteel : null) as ThingWithComps;
                                weapon.TryGetComp<CompQuality>().SetQuality(Rand.Bool ? QualityCategory.Normal : Rand.Bool ? QualityCategory.Good : QualityCategory.Superior, ArtGenerationContext.Colony);
                                pawn.equipment.AddEquipment(weapon);
                                pawn.story.traits.GainTrait(new Trait(AnkhDefs.ankhTraits.RandomElement(), 0, true));
                            }

                            DropPodUtility.DropThingsNear(parms.spawnCenter, map, pawns.Cast<Thing>(), parms.raidPodOpenDelay, false, true, true);
                            Lord lord = LordMaker.MakeNewLord(parms.faction, parms.raidStrategy.Worker.MakeLordJob(parms, map), map, pawns);
                            AvoidGridMaker.RegenerateAvoidGridsFor(parms.faction, map);

                            MoteMaker.ThrowMetaPuffs(CellRect.CenteredOn(parms.spawnCenter, 10), map);

                            if(letter)
                                Find.LetterStack.ReceiveLetter("dick's wrath",
                                    "The god of dicks is angry at your colony.", LetterType.BadUrgent, new GlobalTargetInfo(parms.spawnCenter, map));
                        }
                    }
                }
        };

        static void PrepareDefs()
        {
            MethodInfo shortHashGiver = typeof(ShortHashGiver).GetMethod("GiveShortHash", BindingFlags.NonPublic | BindingFlags.Static);

            StatDefOf.MeleeHitChance.maxValue = float.MaxValue;

            #region MapConditions
            {
                MapConditionDef wrathConditionDef = new MapConditionDef()
                {
                    defName = "wrathConditionDef",
                    conditionClass = typeof(CustomIncidentCall.MapCondition_WrathBombing),
                    label = "wrath of the dead",
                    description = "The gods sent their pawn  down in human form to serve your colony... and you failed him",
                    endMessage = "The gods are satisfied with your pain",
                    preventRain = false,
                    canBePermanent = true
                };
                wrathConditionDef.ResolveReferences();
                wrathConditionDef.PostLoad();
                DefDatabase<MapConditionDef>.Add(wrathConditionDef);
                AnkhDefs.wrathCondition = wrathConditionDef;
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
                AnkhDefs.miracleHeal = miracleHeal;
            }
            #endregion
            #region Buildings
            {
                ThingDef zap = new ThingDef()
                {
                    defName = "ZAPActivator",
                    thingClass = typeof(Buildings.Building_ZAP),
                    label = "ZAP Activator",
                    description = "This device is little more than an altar to Zap, engraved with his jagged yellow symbol. It will defend the ones favored by zap.",
                    size = new IntVec2(1, 1),
                    passability = Traversability.PassThroughOnly,
                    category = ThingCategory.Building,
                    tickerType = TickerType.Rare,
                    selectable = true,
                    designationCategory = DesignationCategoryDefOf.Structure,
                    useHitPoints = false,
                    altitudeLayer = AltitudeLayer.Building,
                    leaveResourcesWhenKilled = true,
                    resourcesFractionWhenDeconstructed = 1,
                    rotatable = false,
                    graphicData = new GraphicData()
                    {
                        texPath = "SH_zap",
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
                AnkhDefs.zapActivator = zap;
            }
            {
                ThingDef therm = new ThingDef()
                {
                    defName = "THERMActivator",
                    thingClass = typeof(Buildings.Building_THERM),
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
                        texPath = "SH_therm",
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
                AnkhDefs.thermActivator = therm;
            }
            {
                ThingDef peg = new ThingDef()
                {
                    defName = "PEGActivator",
                    thingClass = typeof(Buildings.Building_PEG),
                    label = "PEG Activator",
                    description = "This device is little more than an altar to Peg, engraved with her skully sign. It will defend the ones favored by peg.",
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
                        texPath = "SH_peg",
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
                peg.blueprintDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { peg, false, null });
                peg.blueprintDef.ResolveReferences();
                peg.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { peg, true, peg.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                peg.frameDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewFrameDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { peg });
                peg.frameDef.ResolveReferences();
                peg.frameDef.PostLoad();

                peg.ResolveReferences();
                peg.PostLoad();

                shortHashGiver.Invoke(null, new object[] { peg });
                shortHashGiver.Invoke(null, new object[] { minifiedDef });
                shortHashGiver.Invoke(null, new object[] { peg.blueprintDef });
                shortHashGiver.Invoke(null, new object[] { peg.frameDef });

                DefDatabase<ThingDef>.Add(peg);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(peg.blueprintDef);
                DefDatabase<ThingDef>.Add(peg.frameDef);
                peg.designationCategory.ResolveReferences();
                peg.designationCategory.PostLoad();
                AnkhDefs.pegActivator = peg;
            }
            {
                ThingDef repo = new ThingDef()
                {
                    defName = "REPOActivator",
                    thingClass = typeof(Buildings.Building_REPO),
                    label = "REPO Activator",
                    description = "This device is little more than an altar to Repo. Use it to restore.",
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
                        texPath = "SH_repo",
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
                repo.blueprintDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { repo, false, null });
                repo.blueprintDef.ResolveReferences();
                repo.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { repo, true, repo.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                repo.frameDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewFrameDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { repo });
                repo.frameDef.ResolveReferences();
                repo.frameDef.PostLoad();

                repo.ResolveReferences();
                repo.PostLoad();

                shortHashGiver.Invoke(null, new object[] { repo });
                shortHashGiver.Invoke(null, new object[] { minifiedDef });
                shortHashGiver.Invoke(null, new object[] { repo.blueprintDef });
                shortHashGiver.Invoke(null, new object[] { repo.frameDef });

                DefDatabase<ThingDef>.Add(repo);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(repo.blueprintDef);
                DefDatabase<ThingDef>.Add(repo.frameDef);
                repo.designationCategory.ResolveReferences();
                repo.designationCategory.PostLoad();

                AnkhDefs.repoActivator = repo;
            }
            {
                ThingDef bob = new ThingDef()
                {
                    defName = "BOBActivator",
                    thingClass = typeof(Buildings.Building_BOB),
                    label = "BOB Activator",
                    description = "This device is little more than an altar to Bob.",
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
                        texPath = "SH_bob",
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
                bob.blueprintDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { bob, false, null });
                bob.blueprintDef.ResolveReferences();
                bob.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { bob, true, bob.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                bob.frameDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewFrameDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { bob });
                bob.frameDef.ResolveReferences();
                bob.frameDef.PostLoad();

                bob.ResolveReferences();
                bob.PostLoad();

                shortHashGiver.Invoke(null, new object[] { bob });
                shortHashGiver.Invoke(null, new object[] { minifiedDef });
                shortHashGiver.Invoke(null, new object[] { bob.blueprintDef });
                shortHashGiver.Invoke(null, new object[] { bob.frameDef });

                DefDatabase<ThingDef>.Add(bob);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(bob.blueprintDef);
                DefDatabase<ThingDef>.Add(bob.frameDef);
                bob.designationCategory.ResolveReferences();
                bob.designationCategory.PostLoad();
                AnkhDefs.bobActivator = bob;
            }
            {
                ThingDef rootsy = new ThingDef()
                {
                    defName = "ROOTSYActivator",
                    thingClass = typeof(Buildings.Building_ROOTSY),
                    label = "ROOTSY Activator",
                    description = "This device is little more than an altar to Rootsy.",
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
                        texPath = "SH_rootsy",
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
                rootsy.blueprintDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { rootsy, false, null });
                rootsy.blueprintDef.ResolveReferences();
                rootsy.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { rootsy, true, rootsy.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                rootsy.frameDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewFrameDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { rootsy });
                rootsy.frameDef.ResolveReferences();
                rootsy.frameDef.PostLoad();

                rootsy.ResolveReferences();
                rootsy.PostLoad();

                shortHashGiver.Invoke(null, new object[] { rootsy });
                shortHashGiver.Invoke(null, new object[] { minifiedDef });
                shortHashGiver.Invoke(null, new object[] { rootsy.blueprintDef });
                shortHashGiver.Invoke(null, new object[] { rootsy.frameDef });

                DefDatabase<ThingDef>.Add(rootsy);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(rootsy.blueprintDef);
                DefDatabase<ThingDef>.Add(rootsy.frameDef);
                rootsy.designationCategory.ResolveReferences();
                rootsy.designationCategory.PostLoad();
                AnkhDefs.rootsyActivator = rootsy;
            }
            {
                ThingDef humour = new ThingDef()
                {
                    defName = "HUMOURActivator",
                    thingClass = typeof(Buildings.Building_HUMOUR),
                    label = "HUMOUR Activator",
                    description = "This device is little more than an altar to Humour.",
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
                        texPath = "SH_humour",
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
                humour.blueprintDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { humour, false, null });
                humour.blueprintDef.ResolveReferences();
                humour.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { humour, true, humour.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                humour.frameDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewFrameDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { humour });
                humour.frameDef.ResolveReferences();
                humour.frameDef.PostLoad();

                humour.ResolveReferences();
                humour.PostLoad();

                shortHashGiver.Invoke(null, new object[] { humour });
                shortHashGiver.Invoke(null, new object[] { minifiedDef });
                shortHashGiver.Invoke(null, new object[] { humour.blueprintDef });
                shortHashGiver.Invoke(null, new object[] { humour.frameDef });

                DefDatabase<ThingDef>.Add(humour);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(humour.blueprintDef);
                DefDatabase<ThingDef>.Add(humour.frameDef);
                humour.designationCategory.ResolveReferences();
                humour.designationCategory.PostLoad();
                AnkhDefs.humourActivator = humour;
            }
            {
                ThingDef dorf = new ThingDef()
                {
                    defName = "DORFActivator",
                    thingClass = typeof(Buildings.Building_DORF),
                    label = "DORF Activator",
                    description = "This device is little more than an altar to Dorf.",
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
                        texPath = "SH_dorf",
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
                dorf.blueprintDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { dorf, false, null });
                dorf.blueprintDef.ResolveReferences();
                dorf.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { dorf, true, dorf.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                dorf.frameDef = (ThingDef)typeof(ThingDefGenerator_Buildings).GetMethod("NewFrameDef_Thing", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { dorf });
                dorf.frameDef.ResolveReferences();
                dorf.frameDef.PostLoad();

                dorf.ResolveReferences();
                dorf.PostLoad();

                shortHashGiver.Invoke(null, new object[] { dorf });
                shortHashGiver.Invoke(null, new object[] { minifiedDef });
                shortHashGiver.Invoke(null, new object[] { dorf.blueprintDef });
                shortHashGiver.Invoke(null, new object[] { dorf.frameDef });

                DefDatabase<ThingDef>.Add(dorf);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(dorf.blueprintDef);
                DefDatabase<ThingDef>.Add(dorf.frameDef);
                dorf.designationCategory.ResolveReferences();
                dorf.designationCategory.PostLoad();
                AnkhDefs.dorfActivator = dorf;
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
                            baseMoodEffect = -20f
                        }
                    }
                };
                fnarghWrath.ResolveReferences();
                fnarghWrath.PostLoad();
                shortHashGiver.Invoke(null, new object[] { fnarghWrath });
                DefDatabase<ThoughtDef>.Add(fnarghWrath);
                AnkhDefs.fnarghWrath = fnarghWrath;
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
                            baseMoodEffect = 20f
                        }
                    }
                };
                fnarghFavor.ResolveReferences();
                fnarghFavor.PostLoad();
                shortHashGiver.Invoke(null, new object[] { fnarghFavor });
                DefDatabase<ThoughtDef>.Add(fnarghFavor);
                AnkhDefs.fnarghFavor = fnarghFavor;
            }
            #endregion
            #region Traits
            {
                TraitDef fiveKnuckleShuffle = new TraitDef()
                {
                    defName = "fiveKnuckleShuffle",
                    label = "iamdar's Five Knuckle Shuffle",
                    description = "Downward Dick's good good touchin' has enhanced your focus and manipulation!",
                    degreeDatas = new List<TraitDegreeData>()
                    {
                        new TraitDegreeData()
                        {
                            label = "iamdar's Five Knuckle Shuffle",
                            description = "Downward Dick's good good touchin' has enhanced your focus and manipulation!"
                        }
                    }
                };
                fiveKnuckleShuffle.ResolveReferences();
                fiveKnuckleShuffle.PostLoad();
                shortHashGiver.Invoke(null, new object[] { fiveKnuckleShuffle });
                DefDatabase<TraitDef>.Add(fiveKnuckleShuffle);
                AnkhDefs.ankhTraits.Add(fiveKnuckleShuffle);
                AnkhDefs.fiveKnuckleShuffle = fiveKnuckleShuffle;
            }
            {
                TraitDef coneOfShame = new TraitDef()
                {
                    defName = "coneOfShame",
                    label = "chiky's Cone of Shame",
                    description = " Downward Dick's good good touchin' has enhanced your focus and your visual acuity!",
                    degreeDatas = new List<TraitDegreeData>()
                    {
                        new TraitDegreeData()
                        {
                            label = "chiky's Cone of Shame",
                            description = " Downward Dick's good good touchin' has enhanced your focus and your visual acuity!"
                        }
                    }
                };
                coneOfShame.ResolveReferences();
                coneOfShame.PostLoad();
                shortHashGiver.Invoke(null, new object[] { coneOfShame });
                DefDatabase<TraitDef>.Add(coneOfShame);
                AnkhDefs.ankhTraits.Add(coneOfShame);
                AnkhDefs.coneOfShame = coneOfShame;
            }
            {
                TraitDef thrustsOfVeneration = new TraitDef()
                {
                    defName = "thrustsOfVeneration",
                    label = "Ucefzach's Thrusts",
                    description = "Downward Dick's good good touchin' has made you a more effective warrior!",
                    degreeDatas = new List<TraitDegreeData>()
                    {
                        new TraitDegreeData()
                        {
                            label = "Ucefzach's Thrusts",
                            description = "Downward Dick's good good touchin' has made you a more effective warrior!",
                            statOffsets = new List<StatModifier>()
                            {
                                new StatModifier()
                                {
                                    stat = StatDefOf.MeleeHitChance,
                                    value = 0.5f
                                },
                                new StatModifier()
                                {
                                    stat = StatDefOf.AimingDelayFactor,
                                    value = -0.5f
                                }
                            }
                        }
                    }
                };
                thrustsOfVeneration.ResolveReferences();
                thrustsOfVeneration.PostLoad();
                shortHashGiver.Invoke(null, new object[] { thrustsOfVeneration });
                DefDatabase<TraitDef>.Add(thrustsOfVeneration);
                AnkhDefs.ankhTraits.Add(thrustsOfVeneration);
            }
            {
                TraitDef armoredTouch = new TraitDef()
                {
                    defName = "armoredTouch",
                    label = "Southpond's En-Armored Touch",
                    description = "Downward Dick's good good touchin' has surrounded you with a magical armored barrier!",
                    degreeDatas = new List<TraitDegreeData>()
                    {
                        new TraitDegreeData()
                        {
                            label = "Southpond's En-Armored Touch",
                            description = "Downward Dick's good good touchin' has surrounded you with a magical armored barrier!",
                            statOffsets = new List<StatModifier>()
                            {
                                new StatModifier()
                                {
                                    stat = StatDefOf.ArmorRating_Blunt,
                                    value = 0.5f
                                },
                                new StatModifier()
                                {
                                    stat = StatDefOf.ArmorRating_Electric,
                                    value = 0.5f
                                },
                                new StatModifier()
                                {
                                    stat = StatDefOf.ArmorRating_Heat,
                                    value = 0.5f
                                },
                                new StatModifier()
                                {
                                    stat = StatDefOf.ArmorRating_Sharp,
                                    value = 0.5f
                                }
                            }
                        }
                    }
                };
                armoredTouch.ResolveReferences();
                armoredTouch.PostLoad();
                shortHashGiver.Invoke(null, new object[] { armoredTouch });
                DefDatabase<TraitDef>.Add(armoredTouch);
                AnkhDefs.ankhTraits.Add(armoredTouch);
            }
            {
                TraitDef teaAndScones = new TraitDef()
                {
                    defName = "teaAndScones",
                    label = "maebak's tea and scones",
                    description = "Downward Dick's good good touchin' has increased your healing rate!",
                    degreeDatas = new List<TraitDegreeData>()
                    {
                        new TraitDegreeData()
                        {
                            label = "maebak's tea and scones",
                            description = "Downward Dick's good good touchin' has increased your healing rate!",
                            statOffsets = new List<StatModifier>()
                            {
                                new StatModifier()
                                {
                                    stat = StatDefOf.ImmunityGainSpeed,
                                    value = 1.5f
                                }
                            }
                        }
                    }
                };
                teaAndScones.ResolveReferences();
                teaAndScones.PostLoad();
                shortHashGiver.Invoke(null, new object[] { teaAndScones });
                DefDatabase<TraitDef>.Add(teaAndScones);
                AnkhDefs.ankhTraits.Add(teaAndScones);
                AnkhDefs.teaAndScones = teaAndScones;
            }
            #endregion
            #region Hediffs
            {
                HediffDef fiveKnuckleShuffleHediff = new HediffDef()
                {
                    defName = "fiveKnuckleShuffleHediff",
                    label = "iamdar's five knuckle shuffle",
                    description = "Whatever.. pladd tell me something",
                    hediffClass = typeof(Hediff),
                    stages = new List<HediffStage>()
                    {
                        new HediffStage()
                        {
                            capMods = new List<PawnCapacityModifier>()
                            {
                                new PawnCapacityModifier()
                                {
                                    capacity = PawnCapacityDefOf.Consciousness,
                                    offset = 0.4f
                                },
                                new PawnCapacityModifier()
                                {
                                    capacity = PawnCapacityDefOf.Manipulation,
                                    offset = 0.4f
                                }
                            },
                            everVisible = false
                        }
                    }
                };
                fiveKnuckleShuffleHediff.ResolveReferences();
                fiveKnuckleShuffleHediff.PostLoad();
                shortHashGiver.Invoke(null, new object[] { fiveKnuckleShuffleHediff });
                DefDatabase<HediffDef>.Add(fiveKnuckleShuffleHediff);
                AnkhDefs.fiveKnuckleShuffleHediff = fiveKnuckleShuffleHediff;
            }
            {
                HediffDef coneOfShameHediff = new HediffDef()
                {
                    defName = "coneOfShameHediff",
                    label = "chiky's Cone of Shame",
                    description = "Whatever.. pladd tell me something",
                    hediffClass = typeof(Hediff),
                    stages = new List<HediffStage>()
                    {
                        new HediffStage()
                        {
                            capMods = new List<PawnCapacityModifier>()
                            {
                                new PawnCapacityModifier()
                                {
                                    capacity = PawnCapacityDefOf.Consciousness,
                                    offset = 0.4f
                                },
                                new PawnCapacityModifier()
                                {
                                    capacity = PawnCapacityDefOf.Sight,
                                    offset = 0.4f
                                }
                            },
                            everVisible = false
                        }
                    }
                };
                coneOfShameHediff.ResolveReferences();
                coneOfShameHediff.PostLoad();
                shortHashGiver.Invoke(null, new object[] { coneOfShameHediff });
                DefDatabase<HediffDef>.Add(coneOfShameHediff);
                AnkhDefs.coneOfShameHediff = coneOfShameHediff;
            }
            #endregion
        }

        private static void SendWrathLetter(string name, bool possessive, GlobalTargetInfo info) => 
            Find.LetterStack.ReceiveLetter("wrath of " + name, name + " died, prepare to meet " + (possessive ? "his" : "her") + " wrath", LetterType.BadUrgent, info);

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

        public void WaitAndExecute(Action action) => StartCoroutine(WaitAndExecuteCoroutine(action));

        public IEnumerator WaitAndExecuteCoroutine(Action action)
        {
            yield return 100;
            action();
        }
    }
}