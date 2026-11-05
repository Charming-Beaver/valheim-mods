using UnityEngine;

namespace VillageWorkers
{
    public static class Names
    {
        private static readonly string[] Male =
        {
            "Bjorn", "Erik", "Leif", "Ragnar", "Sven", "Gunnar", "Harald", "Ivar", "Ulf", "Torsten", "Halvar", "Sigurd",
            "Arne", "Einar", "Knut", "Olaf", "Rolf", "Stig", "Vidar", "Brandr", "Hakon", "Ketil", "Orm", "Thorvald",
            "Asger", "Frode", "Geir", "Hjalmar", "Njal", "Snorri"
        };

        private static readonly string[] Female =
        {
            "Astrid", "Freydis", "Gudrun", "Helga", "Ingrid", "Sigrid", "Thyra", "Ylva", "Runa", "Solveig", "Ragnhild",
            "Brynhild", "Dagny", "Eira", "Frida", "Gunhild", "Hilda", "Inga", "Liv", "Signy", "Tove", "Unn", "Vigdis",
            "Aslaug", "Gerd", "Hallveig", "Jorunn", "Ragna", "Svala", "Torunn"
        };

        public static string Random(bool female)
        {
            string[] list = female ? Female : Male;
            return list[UnityEngine.Random.Range(0, list.Length)];
        }
    }
}
