using System;
using System.Collections.Generic;

namespace FpkgExfat
{
    internal sealed class TrophyItem
    {
        internal int Id;
        internal string Name = string.Empty;
        internal string Detail = string.Empty;
        internal string Grade = string.Empty;
        internal string GroupId = string.Empty;
        internal string GroupName = string.Empty;
        internal bool Unlocked;
        internal bool StateKnown;

        internal string GradeName
        {
            get
            {
                switch ((Grade ?? string.Empty).ToUpperInvariant())
                {
                    case "P": return "Platinum";
                    case "G": return "Gold";
                    case "S": return "Silver";
                    case "B": return "Bronze";
                    default: return "Unknown";
                }
            }
        }

        internal string StatusName
        {
            get
            {
                if (!StateKnown) return "Unknown";
                return Unlocked ? "Unlocked" : "Locked";
            }
        }
    }

    internal sealed class TrophyListResult
    {
        internal string Ppsa = string.Empty;
        internal string Npwr = string.Empty;
        internal string GameTitle = string.Empty;
        internal string HomeId = string.Empty;
        internal string CatalogSource = string.Empty;
        internal int Total;
        internal int UnlockedCount;
        internal bool StateKnown;
        internal bool UsedUdsRoute;
        internal bool CommitPending;
        internal readonly List<TrophyItem> Trophies = new List<TrophyItem>();

        internal int LockedCount
        {
            get
            {
                int count = 0;
                foreach (TrophyItem trophy in Trophies)
                    if (!trophy.Unlocked) count++;
                return count;
            }
        }
    }
}
