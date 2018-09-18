using Verse;

namespace Ankh
{
    public class StringWrapper : IExposable
    {
        public string value;

        public void ExposeData() => Scribe_Values.Look(value: ref this.value, label: "stringWrapper");

        public static implicit operator string(StringWrapper sw) => sw.value;
        public static implicit operator StringWrapper(string s) => new StringWrapper() { value = s };
    }
}