using System;
using System.Collections;
using Verse;

namespace Ankh
{
    public class StringWrapper : IExposable
    {
        public string value;

        public void ExposeData() => Scribe_Values.Look(ref this.value, "stringWrapper");

        public static implicit operator string(StringWrapper sw) => sw.value;
        public static implicit operator StringWrapper(string s) => new StringWrapper() { value = s };
    }
}