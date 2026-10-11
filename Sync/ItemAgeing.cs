using System;

namespace SailwindCoop.Sync
{
    /// <summary>
    /// Values the host's world moves a little every frame for as long as the item exists: food
    /// drying and spoiling, a soup spoiling, a log burning down. A receiver needs them at the points
    /// where the game reads them, not every frame, so such a value counts as changed only after a
    /// visible step or when it passes one of the game's own marks.
    /// </summary>
    internal static class ItemAgeing
    {
        internal const float Step = 0.01f;
        /// <summary>Between "rotten" and fully spoiled the game blends the look and the harm of a bite.</summary>
        internal const float RottenStep = 0.002f;
        internal const float Rotten = 0.9f;
        /// <summary>Past fully spoiled.</summary>
        internal const float SpoiledStep = 0.1f;
        internal const float HeatStep = 0.02f;
        /// <summary>A log starts at 200; nothing reads the amount but "is anything left".</summary>
        internal const float FuelStep = 2f;

        /// <summary>The two values fall on different sides of a mark the game compares with.</summary>
        internal static bool Crossed(float a, float b, float mark)
            => (a > mark) != (b > mark) || (a >= mark) != (b >= mark);

        internal static bool Dried(float sent, float now)
            => Math.Abs(now - sent) >= Step || Crossed(sent, now, 0f) || Crossed(sent, now, 0.7f) ||
               Crossed(sent, now, 0.99f) || Crossed(sent, now, 1f);

        /// <summary>Spoiling has no upper limit in the game. Past 1 the look no longer changes, but
        /// the amount is still read: it goes into a soup with the food, and into the number of flies.</summary>
        internal static bool Spoiled(float sent, float now)
        {
            float top = Math.Max(sent, now);
            float step = top > 1f ? SpoiledStep : top > Rotten ? RottenStep : Step;
            return Math.Abs(now - sent) >= step || Crossed(sent, now, 0.2f) || Crossed(sent, now, Rotten) || Crossed(sent, now, 1f);
        }

        /// <summary>The heat food and a kettle keep after the stove: it fades for minutes. The game
        /// reads it at 0.5 (food steams) and at 1 (a kettle boils), and caps it at 2.</summary>
        internal static bool Heat(float sent, float now)
            => Math.Abs(now - sent) >= HeatStep || Crossed(sent, now, 0f) || Crossed(sent, now, 0.5f) ||
               Crossed(sent, now, 1f) || Crossed(sent, now, 2f);

        /// <summary>A soup keeps its spoiling as an amount; the game reads it as a share of the energy in the pot.</summary>
        internal static bool SoupSpoiled(float sent, float now, float energy)
            => energy > 0f ? Spoiled(sent / energy, now / energy) : !sent.Equals(now);

        internal static bool Fuel(float sent, float now)
            => Math.Abs(now - sent) >= FuelStep || Crossed(sent, now, 0f);

        /// <summary>The health of an item in the host's stream: a burning log and a burning lamp
        /// (<paramref name="lampFull"/> above 0) lose it every frame and go by steps; everything
        /// else is a player's change and is exact.</summary>
        internal static bool Health(float sent, float now, bool burningLog, float lampFull)
            => burningLog ? Fuel(sent, now) : lampFull > 0f ? Lamp(sent, now, lampFull) : !sent.Equals(now);

        /// <summary>A burning lamp: the game shows its fuel as a whole percent of the full amount
        /// (<c>ShipItemLight.initialHealth</c>) and puts the lamp out at zero.</summary>
        internal static bool Lamp(float sent, float now, float full)
            => full > 0f ? Math.Round(sent / full * 100f) != Math.Round(now / full * 100f) || Crossed(sent, now, 0f)
                         : !sent.Equals(now);
    }
}
