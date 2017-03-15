using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Verse;

namespace Ankh
{
    public struct InstanceVariables
    {
        public int seed;
        [XmlIgnore]
        public Dictionary<string, float> moodTracker;
        [XmlIgnore]
        public Dictionary<int, List<string[]>> scheduler;
        public List<string> deadWraths;


        public SerializeableKeyValue<int, List<string[]>>[] SchedulerSerialized
        {
            get => SerializeDictionary(this.scheduler);
            set
            {
                this.scheduler = new Dictionary<int, List<string[]>>();
                foreach (SerializeableKeyValue<int, List<string[]>> item in value)
                {
                    this.scheduler.Add(item.Key, item.Value);
                }
            }
        }

        public SerializeableKeyValue<string, float>[] MoodTrackerSerialized
        {
            get => SerializeDictionary(this.moodTracker);
            set
            {
                this.moodTracker = new Dictionary<string, float>();
                foreach (SerializeableKeyValue<string, float> item in value)
                {
                    this.moodTracker.Add(item.Key, item.Value);
                }
            }
        }

        private SerializeableKeyValue<T1,T2>[] SerializeDictionary<T1,T2>(Dictionary<T1,T2> dictionary)
        {
            List<SerializeableKeyValue<T1,T2>> list = new List<SerializeableKeyValue<T1, T2>>();
            if (dictionary != null)
            {
                list.AddRange(dictionary.Keys.Select(key => new SerializeableKeyValue<T1, T2>() { Key = key, Value = dictionary[key] }));
            }
            return list.ToArray();
        }

        public class SerializeableKeyValue<T1, T2>
        {
            public T1 Key { get; set; }
            public T2 Value { get; set; }
        }
    }
}