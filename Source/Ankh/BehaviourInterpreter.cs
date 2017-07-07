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

        public static Dictionary<string, Action<bool, bool>> gods;

        private static List<Action<string, bool>> wrathActions;

        private static List<Action> congratsActions;

        public InstanceVariables instanceVariableHolder;

        static BehaviourInterpreter()
        {
            GameObject initializer = new GameObject("Ankh_Interpreter");

            configFullPath = Path.Combine(LoadedModManager.RunningMods.First(mcp => mcp.assemblies.loadedAssemblies.Contains(typeof(BehaviourInterpreter).Assembly)).RootDir, "Assemblies");
            
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

            GodsOfRimworld modHandler = LoadedModManager.GetMod<GodsOfRimworld>();

            modHandler.GetSettings<GodSettings>().enabledGods = new Dictionary<StringWrapper, bool>();

            foreach (string s in gods.Keys)
            {
                if (!modHandler.GetSettings<GodSettings>().enabledGods.ContainsKey(s))
                    modHandler.GetSettings<GodSettings>().enabledGods.Add(s, true);
            }

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
                    instanceVariable.altarState = 0;

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


                        if ((Find.TickManager.TicksGame / (GenDate.TicksPerHour / 8) > this.instanceVariableHolder.lastTickTick / (GenDate.TicksPerHour / 8)))
                        {
                            IEnumerable<int> keyList = this.instanceVariableHolder.scheduler.Keys.Where(i => i < Find.TickManager.TicksGame);
                            List<string[]> actionList = keyList.SelectMany(i => this.instanceVariableHolder.scheduler[i]).ToList();
                            keyList.ToList().ForEach(i => this.instanceVariableHolder.scheduler.Remove(i));

                            actionList.ForEach(a =>
                            {
                                try
                                {
                                    Log.Message(string.Join(" | ", a) + actionList.Count);
                                    ExecuteScheduledCommand(a);
                                    actionList.Where(l => l != a && l[0].Equals(a[0]) && (l.Length == 1 || l[1].Equals(a[1]) && l[2].Equals(a[2]))).ToList().ForEach(l =>
                                    {
                                        Log.Message("Re-adding to avoid spamming");
                                        AddToScheduler(GenDate.TicksPerHour / 2 - 1, l);
                                        actionList.Remove(l);
                                    });
                                }
                                catch (Exception e)
                                {
                                    Debug.Log(e.Message + "\n" + e.StackTrace);
                                    AddToScheduler(10, a);
                                }
                            });

                            Find.ColonistBar.GetColonistsInOrder().ForEach(p =>
                            {
                                if (p.story.traits.HasTrait(AnkhDefOf.coneOfShame) && !p.health.hediffSet.HasHediff(AnkhDefOf.coneOfShameHediff))
                                    p.health.hediffSet.AddDirect(HediffMaker.MakeHediff(AnkhDefOf.coneOfShameHediff, p));
                                if (p.story.traits.HasTrait(AnkhDefOf.fiveKnuckleShuffle) && !p.health.hediffSet.HasHediff(AnkhDefOf.fiveKnuckleShuffleHediff))
                                    p.health.hediffSet.AddDirect(HediffMaker.MakeHediff(AnkhDefOf.fiveKnuckleShuffleHediff, p));
                            });

                            if (Find.TickManager.TicksGame / (GenDate.TicksPerHour / 2) > this.instanceVariableHolder.lastTickTick / (GenDate.TicksPerHour / 2))
                            {
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
                                                    if (points >= cost || split[0].EqualsIgnoreCase("itspladd") || split[0].EqualsIgnoreCase("erdelf") || (split[0].EqualsIgnoreCase("serphentalis") && points >= cost / 2))
                                                        AddToScheduler(favor ? 1 : Find.TickManager.TicksGame > GenDate.TicksPerDay * 3 ? (this.instanceVariableHolder.altarState == 0 ? 1 : GenDate.TicksPerDay) : /*GenDate.TicksPerDay * 3 - Find.TickManager.TicksGame*/1, "callTheGods", split[2].ToLower(), favor.ToString(), true.ToString());
                                            }
                                        }
                                    );

                                if (Find.TickManager.TicksGame / GenDate.TicksPerHour > this.instanceVariableHolder.lastTickTick / GenDate.TicksPerHour)
                                {
                                    Find.ColonistBar.GetColonistsInOrder().Where(p => !p.Dead && p.story.traits.HasTrait(AnkhDefOf.teaAndScones)).ToList().ForEach(p =>
                                    {
                                        int i = 2;

                                        List<Hediff_Injury> hediffs = p.health.hediffSet.GetHediffs<Hediff_Injury>().Where(hi => !hi.IsOld()).ToList().ListFullCopy();
                                        while (i > 0 && hediffs.Count > 0)
                                        {
                                            Hediff_Injury hediff = hediffs.First();
                                            float val = Math.Min(hediff.Severity, i);
                                            i -= Mathf.RoundToInt(val);
                                            hediff.Heal(val);
                                            hediffs.Remove(hediff);
                                        }
                                    });

                                    if (Find.TickManager.TicksGame / (GenDate.TicksPerDay) > this.instanceVariableHolder.lastTickTick / (GenDate.TicksPerDay))
                                    {
                                        Log.Message("Day: " + GenDate.DaysPassed);

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

                                        if ((Find.TickManager.TicksGame / (GenDate.TicksPerTwelfth) > this.instanceVariableHolder.lastTickTick / (GenDate.TicksPerTwelfth)) && Find.TickManager.TicksGame > 0)
                                        {
                                            if (Find.ColonistBar.GetColonistsInOrder().Any(p => !p.Dead))
                                            {
                                                AddToScheduler(5, "survivalReward");
                                            }
                                        }
                                    }
                                }
                            }
                            this.instanceVariableHolder.lastTickTick = Find.TickManager.TicksGame;
                        }
                        
                        if (staticVariables.instaResearch > 0)
                            if (Find.ResearchManager.currentProj != null)
                            {
                                Find.ResearchManager.ResearchPerformed(400f / 0.007f, null);
                                staticVariables.instaResearch--;
                            }
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
                    else
                        Log.Message(parameters[1] + " is not in the active god pool");
                    break;
                case "survivalReward":
                    Find.LetterStack.ReceiveLetter("survival reward", "You survived a month on this rimworld, the gods are pleased", LetterDefOf.Good);
                    congratsActions.RandomElement().Invoke();
                    break;
                case "wrathCall":
                    CustomIncidentClasses.WeatherEvent_WrathBombing.erdelf = parameters[1].EqualsIgnoreCase("erdelf");
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
                    AnkhDefOf.miracleHeal.Worker.TryExecute(null);
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
                        workerClass = typeof(CustomIncidentClasses.PawnGroupKindWorker_Wrath)
                    },
                }.GeneratePawns(new PawnGroupMakerParms()
                {
                    tile = (parms.target as Map).Tile,
                    faction = parms.faction,
                    points = parms.points,
                    generateFightersOnly = true,
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
                Find.AnyPlayerHomeMap.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(AnkhDefOf.wrathCondition, GenDate.TicksPerDay * 1, 0));
                SendWrathLetter(s, b, GlobalTargetInfo.Invalid);
            });
        }

        public static void AddGods() => gods = new Dictionary<string, Action<bool, bool>>
        {
                {
                    "zap",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            staticVariables.zapCount++;

                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(AnkhDefOf.zapActivator)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter("zap's favor",
                                    "The god of lightning shows mercy on your colony. He commands the fire in the sky to obey you for once", LetterDefOf.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
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
                                    "The god of lightning is angry at your colony. He commands the fire in the sky to strike down on " + p.NameStringShort, LetterDefOf.BadUrgent, p);
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
                                    "The god of war shows mercy on your colony. A piece of equipment below his godly standards got thrown on your colony", LetterDefOf.Good, thing);
                        }
                        else
                        {
                            IncidentParms incidentParms = StorytellerUtility.DefaultParmsNow(Find.Storyteller.def, IncidentCategory.ThreatBig, Find.AnyPlayerHomeMap);

                            Letter letterobj = LetterMaker.MakeLetter("sparto's wrath",
                                    "The god of war is angry at your colony. She commands the locals of this world to attack", LetterDefOf.BadUrgent);

                            CustomIncidentClasses.CallEnemyRaid(incidentParms, letter ? letterobj : null);

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

                            ThingDef peg = AnkhDefOf.pegActivator;
                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(peg)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter("peg's favor",
                                    "The god of pirates shows mercy on your colony. He commands the fires of this world to defend you", LetterDefOf.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
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
                                    "The god of pirates is angry at your colony. He commands the fires to strike down on your colony", LetterDefOf.BadUrgent);
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
                            ThingDef peg = AnkhDefOf.repoActivator;
                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(peg)).ToList();
                            if (letter)
                                Find.LetterStack.ReceiveLetter("repo's favor",
                                    "The god of organs shows mercy on your colony", LetterDefOf.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
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
                                        "The god of organs is angry at your colony. He commands the " + part.def.LabelCap.ToLower() + " of " + p.NameStringShort + " to damage itself", LetterDefOf.BadUrgent, p);
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
                            ThingDef peg = AnkhDefOf.bobActivator;
                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(peg)).ToList();
                            if (letter)
                                Find.LetterStack.ReceiveLetter("bob's favor",
                                    "The god of buildings shows mercy on your colony", LetterDefOf.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
                        }else
                        {
                            List<Building> walls = Find.AnyPlayerHomeMap.listerBuildings.AllBuildingsColonistOfDef(ThingDefOf.Wall).ToList();

                            if(walls == null || walls.Count() < 2)
                                throw new Exception();

                            GlobalTargetInfo target = default(GlobalTargetInfo);
                            for(int i = 0; i<2; i++)
                            {
                                if(walls.TryRandomElement(out Building wall))
                                {
                                    target = new GlobalTargetInfo(wall.Position, wall.Map);
                                    wall.Destroy();
                                    walls.Remove(wall);
                                }
                            }
                            if (letter)
                                Find.LetterStack.ReceiveLetter("bob's wrath",
                                        "The god of buildings is angry at your colony.", LetterDefOf.BadUrgent, target);
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
                            ThingDef peg = AnkhDefOf.rootsyActivator;
                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(peg)).ToList();
                            if (letter)
                                Find.LetterStack.ReceiveLetter("rootsy's favor",
                                    "The god of plants shows mercy on your colony", LetterDefOf.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
                        } else
                        {
                            List<Plant> list = new List<Plant>();
                            foreach (Map map in Find.Maps.Where((Map map) => map.ParentFaction == Faction.OfPlayer))
                                list.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSource).Where((Thing thing) => thing is Plant && thing.def.plant.growDays <= 16f && (thing as Plant).LifeStage == PlantLifeStage.Growing && thing.Map.zoneManager.ZoneAt(thing.Position) is Zone_Growing && !thing.def.defName.EqualsIgnoreCase(ThingDefOf.PlantGrass.defName)).Cast<Plant>());
                            if (list.Count < 10)
                                throw new Exception();
                            list = list.InRandomOrder().Take(list.Count > 30 ? 30 : list.Count).ToList();

                            list.ForEach(plant =>
                            {
                                plant.CropBlighted();
                                list.Remove(plant);
                            });

                            if(letter)
                                Find.LetterStack.ReceiveLetter("rootsy's wrath",
                                     "The god of flowers is angry at your colony. He commands the roots under your colony to blight", LetterDefOf.BadNonUrgent);

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
                            thing.SetFactionDirect(Faction.OfPlayer);
                            GenSpawn.Spawn(thing, position, map);
                            Vector3 vec = position.ToVector3();
                            MoteMaker.ThrowSmoke(vec, map, 5);
                            MoteMaker.ThrowMetaPuff(vec, map);

                            if (letter)
                                Find.LetterStack.ReceiveLetter("fondle's favor",
                                    "The god of art shows mercy on your colony.", LetterDefOf.Good, thing);
                        }else
                        {
                            Map map = Find.AnyPlayerHomeMap;

                            map.listerThings.AllThings.Where(t => t.TryGetComp<CompQuality>()?.Quality > QualityCategory.Awful && (t.Position.GetZone(map) is Zone_Stockpile || t.Faction == Faction.OfPlayer)).TryRandomElement(out Thing thing);

                            if(thing == null)
                                throw new Exception();

                            thing.TryGetComp<CompQuality>().SetQuality(QualityCategory.Awful, ArtGenerationContext.Colony);

                            if(letter)
                                Find.LetterStack.ReceiveLetter("fondle's wrath",
                                     "The god of art is angry at your colony.", LetterDefOf.BadNonUrgent, thing);
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
                        if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 root, map, 50f))
                        {
                            throw new Exception();
                        }
                        Pawn pawn = PawnGenerator.GeneratePawn(pawnKindDef);

                        IntVec3 loc = CellFinder.RandomClosewalkCellNear(root, map, 10);
                        GenSpawn.Spawn(pawn, loc, map);

                        if(favor)
                        {
                            pawn.SetFaction(Faction.OfPlayer);
                            pawn.jobs.TryTakeOrderedJob(new Job(JobDefOf.GotoWander, map.areaManager.Home.ActiveCells.Where(iv => iv.Standable(map)).RandomElement()));
                            if (letter)
                                Find.LetterStack.ReceiveLetter("moo's favor",
                                     "The god of animals shows mercy on your colony. He commands his subordinate to be devoted to your colony", LetterDefOf.Good, pawn);

                        } else
                        {
                            pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent, null, true, false, null);
                            if(letter)
                                Find.LetterStack.ReceiveLetter("moo's wrath",
                                    "The god of animals is angry at your colony. He commands his subordinate to teach you a lesson", LetterDefOf.BadUrgent, pawn);

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
                                         "The god of commerce shows mercy on your colony.", LetterDefOf.Good);
                        } else
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            List<Thing> silver = map.listerThings.ThingsOfDef(ThingDefOf.Silver);

                            if(silver.Sum(t => t.stackCount) < 200)
                                throw new Exception();

                            int i = 200;

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
                            for(int c = 0; c<50; c++)
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
                                    "The god of commerce is angry at your colony.", LetterDefOf.BadNonUrgent);
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
                                 p.needs.mood.thoughts.memories.TryGainMemory(AnkhDefOf.fnarghFavor);
                                 if (letter)
                                     Find.LetterStack.ReceiveLetter("fnargh's favor",
                                          "The god fnargh shows mercy on your colony. He commands the web of thought to make " + p.NameStringShort + " happy", LetterDefOf.Good, p);
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
                                 p.needs.mood.thoughts.memories.TryGainMemory(AnkhDefOf.fnarghWrath);
                                 p.mindState.mentalStateHandler.TryStartMentalState(DefDatabase<MentalStateDef>.AllDefs.Where(msd => msd != MentalStateDefOf.SocialFighting && msd != MentalStateDefOf.PanicFlee && !msd.defName.EqualsIgnoreCase("GiveUpExit") && msd.Worker.GetType().GetField("otherPawn", (BindingFlags) 60) == null).RandomElement(), "Fnargh's wrath", true, true);
                                 if (letter)
                                     Find.LetterStack.ReceiveLetter("fnargh's wrath",
                                         "The god fnargh is angry at your colony. He commands the web of thought to make " + p.NameStringShort + " mad", LetterDefOf.BadNonUrgent, p);
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

                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(AnkhDefOf.thermActivator)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter("therm's favor",
                                    "The god of fire shows mercy on your colony. He commands the fires of this little world to follow your orders.", LetterDefOf.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
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
                                    "The god therm is angry at your colony. He commands the body of " + p.NameStringShort + " to combust", LetterDefOf.BadUrgent, p);
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
                                    "The god beepboop shows mercy on your colony. The colonists find the missing link to finish their current research.", LetterDefOf.Good);

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
                                    "The god of robots is angry at your colony.", LetterDefOf.BadUrgent, new GlobalTargetInfo(position, map));
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

                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(AnkhDefOf.humourActivator)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter("humour's favor",
                                    "The god of healing shows mercy on your colony.", LetterDefOf.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
                        } else
                        {
                            Pawn p = Find.ColonistBar?.GetColonistsInOrder()?.Where((Pawn x) => !x.Dead && !x.Downed && !x.mindState.mentalStateHandler.InMentalState && !x.jobs.curDriver.asleep).RandomElement();
                            if (p != null)
                            {
                                p.health.AddHediff(HediffDefOf.WoundInfection, p.health.hediffSet.GetRandomNotMissingPart(DamageDefOf.Bullet));
                                if (letter)
                                    Find.LetterStack.ReceiveLetter("humour's wrath",
                                        "The god humour is angry at your colony.", LetterDefOf.BadUrgent, p);
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

                            IntVec3 intVec = map.areaManager.Home.ActiveCells.Where(iv => iv.Standable(map)).RandomElement();
                            for(int i=0;i<5;i++)
                            {
                                if(apparelList.TryRandomElement(out ThingDef apparelDef))
                                {
                                    intVec = intVec.RandomAdjacentCell8Way();
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

                                    GenSpawn.Spawn(apparel, intVec, map);
                                    Vector3 vec = intVec.ToVector3();
                                    MoteMaker.ThrowSmoke(vec, map, 5);
                                    MoteMaker.ThrowMetaPuff(vec, map);
                                }
                            }
                            if (letter)
                                Find.LetterStack.ReceiveLetter("taylor's favor",
                                    "The god of clothing shows mercy on your colony.", LetterDefOf.Good, new GlobalTargetInfo(intVec, map));
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
                                    "The god of clothing is angry at your colony. He commands the clothing on " + p.NameStringShort + " to destroy itself", LetterDefOf.BadUrgent, p);
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

                            List<Thing> activators = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.listerThings.ThingsOfDef(AnkhDefOf.dorfActivator)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter("dorf's favor",
                                    "The god of ming shows mercy on your colony.", LetterDefOf.Good, new GlobalTargetInfo(activators.NullOrEmpty() ? null : activators.RandomElement()));
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
                                    "The god of mining is angry at your colony.", LetterDefOf.BadUrgent, new GlobalTargetInfo(position, map));
                        }
                    }
                },
                {
                    "downward_dick",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {
                            List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder().Where((Pawn x) => !x.Dead && !AnkhDefOf.ankhTraits.TrueForAll(t => x.story.traits.HasTrait(t))).ToList();

                            if(pawns.NullOrEmpty())
                                throw new Exception();

                            pawns.ForEach(p =>
                            {
                                Trait trait = null;
                                do
                                {
                                    trait = new Trait(AnkhDefOf.ankhTraits.RandomElement(), 0, true);
                                    if(!p.story.traits.HasTrait(trait.def))
                                        p.story.traits.GainTrait(trait);
                                }while (!p.story.traits.allTraits.Contains(trait));
                                p.story.traits.allTraits.Remove(trait);
                                p.story.traits.allTraits.Insert(0, trait);
                            });
                            if (letter)
                                Find.LetterStack.ReceiveLetter("dick's favor",
                                    "The god of dicks shows mercy on your colony.", LetterDefOf.Good);
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
                                    workerClass = typeof(CustomIncidentClasses.PawnGroupKindWorker_Wrath)
                                },
                            }.GeneratePawns(new PawnGroupMakerParms()
                            {
                                tile = (parms.target as Map).Tile,
                                faction = parms.faction,
                                points = parms.points,
                                generateFightersOnly = true,
                                raidStrategy = parms.raidStrategy
                            }).ToList();

                            IEnumerable<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading.Where(rd => (rd.addsHediff?.addedPartProps?.isBionic ?? false) && 
                            (rd?.fixedIngredientFilter?.AllowedThingDefs.Any(td => td.techHediffsTags?.Contains("Advanced") ?? false) ?? false) && !rd.appliedOnFixedBodyParts.NullOrEmpty());

                            for(int i = 0; i<pawns.Count;i++)
                            {
                                Pawn colonist = colonists[i];
                                Pawn pawn = pawns[i];

                                pawn.Name = colonist.Name;
                                pawn.story.traits.allTraits = colonist.story.traits.allTraits.ListFullCopy();
                                pawn.story.childhood = colonist.story.childhood;
                                pawn.story.adulthood = colonist.story.adulthood;
                                pawn.skills.skills = colonist.skills.skills.ListFullCopy();
                                pawn.health.hediffSet.hediffs = colonist.health.hediffSet.hediffs.ListFullCopy().Where(hediff => hediff is Hediff_AddedPart).ToList();
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
                                    recipes.Where(rd => rd.appliedOnFixedBodyParts != null).TryRandomElement(out recipe);
                                    BodyPartRecord record;
                                    do
                                    {
                                        record = pawn.health.hediffSet.GetRandomNotMissingPart(DamageDefOf.Bullet);
                                    } while(!recipe.appliedOnFixedBodyParts.Contains(record.def));
                                    recipe.Worker.ApplyOnPawn(pawn, record, null, recipe.fixedIngredientFilter.AllowedThingDefs.Select(td => ThingMaker.MakeThing(td, td.MadeFromStuff ? GenStuff.DefaultStuffFor(td) : null)).ToList());
                                }
                                pawn.equipment.DestroyAllEquipment();
                                ThingDef weaponDef = new ThingDef[] { ThingDef.Named("Gun_AssaultRifle"), ThingDef.Named("Gun_ChargeRifle"), ThingDef.Named("MeleeWeapon_LongSword") }.RandomElement();
                                if(weaponDef.IsRangedWeapon)
                                    pawn.apparel.WornApparel.RemoveAll(ap => ap.def == ThingDefOf.Apparel_ShieldBelt);
                                ThingWithComps weapon = ThingMaker.MakeThing(weaponDef, weaponDef.MadeFromStuff ? ThingDefOf.Plasteel : null) as ThingWithComps;
                                weapon.TryGetComp<CompQuality>().SetQuality(Rand.Bool ? QualityCategory.Normal : Rand.Bool ? QualityCategory.Good : QualityCategory.Superior, ArtGenerationContext.Colony);
                                pawn.equipment.AddEquipment(weapon);
                                pawn.story.traits.GainTrait(new Trait(AnkhDefOf.ankhTraits.RandomElement(), 0, true));
                            }

                            DropPodUtility.DropThingsNear(parms.spawnCenter, map, pawns.Cast<Thing>(), parms.raidPodOpenDelay, false, true, true);
                            Lord lord = LordMaker.MakeNewLord(parms.faction, parms.raidStrategy.Worker.MakeLordJob(parms, map), map, pawns);
                            AvoidGridMaker.RegenerateAvoidGridsFor(parms.faction, map);

                            MoteMaker.ThrowMetaPuffs(CellRect.CenteredOn(parms.spawnCenter, 10), map);

                            if(letter)
                                Find.LetterStack.ReceiveLetter("dick's wrath",
                                    "The god of dicks is angry at your colony.", LetterDefOf.BadUrgent, new GlobalTargetInfo(parms.spawnCenter, map));
                        }
                    }
                }/*,
                {
                    "erdelf",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {

                        } else
                        {

                        }
                    }
                },
                {
                    "pladd",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {

                        } else
                        {

                        }
                    }
                }*/
        };

        static void PrepareDefs()
        {
            MethodInfo shortHashGiver = typeof(ShortHashGiver).GetMethod("GiveShortHash", BindingFlags.NonPublic | BindingFlags.Static);
            Type t = typeof(AnkhDefOf);
            StatDefOf.MeleeHitChance.maxValue = float.MaxValue;

            #region GameConditions
            {
                GameConditionDef wrathConditionDef = new GameConditionDef()
                {
                    defName = "wrathConditionDef",
                    conditionClass = typeof(CustomIncidentClasses.MapCondition_WrathBombing),
                    label = "wrath of the dead",
                    description = "The gods sent their pawn  down in human form to serve your colony... and you failed him",
                    endMessage = "The gods are satisfied with your pain",
                    preventRain = false,
                    canBePermanent = true
                };
                wrathConditionDef.ResolveReferences();
                wrathConditionDef.PostLoad();
                DefDatabase<GameConditionDef>.Add(wrathConditionDef);
                AnkhDefOf.wrathCondition = wrathConditionDef;
                shortHashGiver.Invoke(null, new object[] { wrathConditionDef, t });
            }
            #endregion
            #region Incidents
            {
                IncidentDef miracleHeal = new IncidentDef()
                {
                    defName = "MiracleHeal",
                    label = "miracle heal",
                    targetType = IncidentTargetType.MapPlayerHome,
                    workerClass = typeof(CustomIncidentClasses.MiracleHeal),
                    baseChance = 10
                };
                miracleHeal.ResolveReferences();
                miracleHeal.PostLoad();
                shortHashGiver.Invoke(null, new object[] { miracleHeal, t });
                DefDatabase<IncidentDef>.Add(miracleHeal);
                AnkhDefOf.miracleHeal = miracleHeal;
            }
            {
                IncidentDef altarAppearance = new IncidentDef()
                {
                    defName = "AltarAppearance",
                    label = "altar Appearance",
                    targetType = IncidentTargetType.MapPlayerHome,
                    workerClass = typeof(CustomIncidentClasses.AltarAppearance),
                    baseChance = 10
                };
                altarAppearance.ResolveReferences();
                altarAppearance.PostLoad();
                shortHashGiver.Invoke(null, new object[] { altarAppearance, t });
                DefDatabase<IncidentDef>.Add(altarAppearance);
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

                shortHashGiver.Invoke(null, new object[] { zap, t });
                shortHashGiver.Invoke(null, new object[] { minifiedDef, t });
                shortHashGiver.Invoke(null, new object[] { zap.blueprintDef, t });
                shortHashGiver.Invoke(null, new object[] { zap.frameDef, t });

                DefDatabase<ThingDef>.Add(zap);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(zap.blueprintDef);
                DefDatabase<ThingDef>.Add(zap.frameDef);
                zap.designationCategory.ResolveReferences();
                zap.designationCategory.PostLoad();
                AnkhDefOf.zapActivator = zap;
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

                shortHashGiver.Invoke(null, new object[] { therm, t });
                shortHashGiver.Invoke(null, new object[] { minifiedDef, t });
                shortHashGiver.Invoke(null, new object[] { therm.blueprintDef, t });
                shortHashGiver.Invoke(null, new object[] { therm.frameDef, t });

                DefDatabase<ThingDef>.Add(therm);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(therm.blueprintDef);
                DefDatabase<ThingDef>.Add(therm.frameDef);
                therm.designationCategory.ResolveReferences();
                therm.designationCategory.PostLoad();
                AnkhDefOf.thermActivator = therm;
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

                shortHashGiver.Invoke(null, new object[] { peg, t });
                shortHashGiver.Invoke(null, new object[] { minifiedDef, t });
                shortHashGiver.Invoke(null, new object[] { peg.blueprintDef, t });
                shortHashGiver.Invoke(null, new object[] { peg.frameDef, t });

                DefDatabase<ThingDef>.Add(peg);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(peg.blueprintDef);
                DefDatabase<ThingDef>.Add(peg.frameDef);
                peg.designationCategory.ResolveReferences();
                peg.designationCategory.PostLoad();
                AnkhDefOf.pegActivator = peg;
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

                shortHashGiver.Invoke(null, new object[] { repo, t });
                shortHashGiver.Invoke(null, new object[] { minifiedDef, t });
                shortHashGiver.Invoke(null, new object[] { repo.blueprintDef, t });
                shortHashGiver.Invoke(null, new object[] { repo.frameDef, t });

                DefDatabase<ThingDef>.Add(repo);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(repo.blueprintDef);
                DefDatabase<ThingDef>.Add(repo.frameDef);
                repo.designationCategory.ResolveReferences();
                repo.designationCategory.PostLoad();

                AnkhDefOf.repoActivator = repo;
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

                shortHashGiver.Invoke(null, new object[] { bob, t });
                shortHashGiver.Invoke(null, new object[] { minifiedDef, t });
                shortHashGiver.Invoke(null, new object[] { bob.blueprintDef, t });
                shortHashGiver.Invoke(null, new object[] { bob.frameDef, t });

                DefDatabase<ThingDef>.Add(bob);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(bob.blueprintDef);
                DefDatabase<ThingDef>.Add(bob.frameDef);
                bob.designationCategory.ResolveReferences();
                bob.designationCategory.PostLoad();
                AnkhDefOf.bobActivator = bob;
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

                shortHashGiver.Invoke(null, new object[] { rootsy, t });
                shortHashGiver.Invoke(null, new object[] { minifiedDef, t });
                shortHashGiver.Invoke(null, new object[] { rootsy.blueprintDef, t });
                shortHashGiver.Invoke(null, new object[] { rootsy.frameDef, t });

                DefDatabase<ThingDef>.Add(rootsy);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(rootsy.blueprintDef);
                DefDatabase<ThingDef>.Add(rootsy.frameDef);
                rootsy.designationCategory.ResolveReferences();
                rootsy.designationCategory.PostLoad();
                AnkhDefOf.rootsyActivator = rootsy;
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

                shortHashGiver.Invoke(null, new object[] { humour, t });
                shortHashGiver.Invoke(null, new object[] { minifiedDef, t });
                shortHashGiver.Invoke(null, new object[] { humour.blueprintDef, t });
                shortHashGiver.Invoke(null, new object[] { humour.frameDef, t });

                DefDatabase<ThingDef>.Add(humour);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(humour.blueprintDef);
                DefDatabase<ThingDef>.Add(humour.frameDef);
                humour.designationCategory.ResolveReferences();
                humour.designationCategory.PostLoad();
                AnkhDefOf.humourActivator = humour;
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

                shortHashGiver.Invoke(null, new object[] { dorf, t });
                shortHashGiver.Invoke(null, new object[] { minifiedDef, t });
                shortHashGiver.Invoke(null, new object[] { dorf.blueprintDef, t });
                shortHashGiver.Invoke(null, new object[] { dorf.frameDef, t });

                DefDatabase<ThingDef>.Add(dorf);
                DefDatabase<ThingDef>.Add(minifiedDef);
                DefDatabase<ThingDef>.Add(dorf.blueprintDef);
                DefDatabase<ThingDef>.Add(dorf.frameDef);
                dorf.designationCategory.ResolveReferences();
                dorf.designationCategory.PostLoad();
                AnkhDefOf.dorfActivator = dorf;
            }
            {
                ThingDef sacrificeAltar = new ThingDef()
                {
                    defName = "sacrificeAltar",
                    thingClass = typeof(Buildings.Building_Altar),
                    label = "sacrifice altar",
                    description = "This Altar serves as a way to please the gods.",
                    size = new IntVec2(3, 1),
                    passability = Traversability.Impassable,
                    category = ThingCategory.Building,
                    selectable = true,
                    useHitPoints = false,
                    altitudeLayer = AltitudeLayer.Building,
                    leaveResourcesWhenKilled = true,
                    rotatable = false,
                    graphicData = new GraphicData()
                    {
                        texPath = "HumanAltar",
                        graphicClass = typeof(Graphic_Single),
                        shaderType = ShaderType.CutoutComplex,
                        drawSize = new Vector2(4, 2)
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
                            value = 50
                        }
                    },
                    building = new BuildingProperties()
                    {
                        isInert = true,
                        ignoreNeedsPower = true
                    },
                    inspectorTabs = new List<Type>() { typeof(ITab_Wraths) },
                    hasInteractionCell = true,
                    interactionCellOffset = new IntVec3(0, 0, -1)
                };
                sacrificeAltar.ResolveReferences();
                sacrificeAltar.PostLoad();
                shortHashGiver.Invoke(null, new object[] { sacrificeAltar, t });
                DefDatabase<ThingDef>.Add(sacrificeAltar);
                AnkhDefOf.sacrificeAltar = sacrificeAltar;
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
                shortHashGiver.Invoke(null, new object[] { fnarghWrath, t });
                DefDatabase<ThoughtDef>.Add(fnarghWrath);
                AnkhDefOf.fnarghWrath = fnarghWrath;
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
                shortHashGiver.Invoke(null, new object[] { fnarghFavor, t });
                DefDatabase<ThoughtDef>.Add(fnarghFavor);
                AnkhDefOf.fnarghFavor = fnarghFavor;
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
                shortHashGiver.Invoke(null, new object[] { fiveKnuckleShuffle, t });
                DefDatabase<TraitDef>.Add(fiveKnuckleShuffle);
                AnkhDefOf.ankhTraits.Add(fiveKnuckleShuffle);
                AnkhDefOf.fiveKnuckleShuffle = fiveKnuckleShuffle;
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
                shortHashGiver.Invoke(null, new object[] { coneOfShame, t });
                DefDatabase<TraitDef>.Add(coneOfShame);
                AnkhDefOf.ankhTraits.Add(coneOfShame);
                AnkhDefOf.coneOfShame = coneOfShame;
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
                shortHashGiver.Invoke(null, new object[] { thrustsOfVeneration, t });
                DefDatabase<TraitDef>.Add(thrustsOfVeneration);
                AnkhDefOf.ankhTraits.Add(thrustsOfVeneration);
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
                shortHashGiver.Invoke(null, new object[] { armoredTouch, t });
                DefDatabase<TraitDef>.Add(armoredTouch);
                AnkhDefOf.ankhTraits.Add(armoredTouch);
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
                shortHashGiver.Invoke(null, new object[] { teaAndScones, t });
                DefDatabase<TraitDef>.Add(teaAndScones);
                AnkhDefOf.ankhTraits.Add(teaAndScones);
                AnkhDefOf.teaAndScones = teaAndScones;
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
                shortHashGiver.Invoke(null, new object[] { fiveKnuckleShuffleHediff, t });
                DefDatabase<HediffDef>.Add(fiveKnuckleShuffleHediff);
                AnkhDefOf.fiveKnuckleShuffleHediff = fiveKnuckleShuffleHediff;
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
                shortHashGiver.Invoke(null, new object[] { coneOfShameHediff, t });
                DefDatabase<HediffDef>.Add(coneOfShameHediff);
                AnkhDefOf.coneOfShameHediff = coneOfShameHediff;
            }
            #endregion
            #region Jobs
            {
                JobDef sacrifice = new JobDef()
                {
                    defName = "sacrificeYourself",
                    driverClass = typeof(JobDriver_Sacrifice),
                    reportString = "selfsacrificing"
                };
                sacrifice.ResolveReferences();
                sacrifice.PostLoad();
                shortHashGiver.Invoke(null, new object[] { sacrifice, t });
                DefDatabase<JobDef>.Add(sacrifice);
                AnkhDefOf.sacrificeToAltar = sacrifice;
            }
            #endregion
        }

        private static void SendWrathLetter(string name, bool possessive, GlobalTargetInfo info) => 
            Find.LetterStack.ReceiveLetter("wrath of " + name, name + " died, prepare to meet " + (possessive ? "his" : "her") + " wrath", LetterDefOf.BadUrgent, info);

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