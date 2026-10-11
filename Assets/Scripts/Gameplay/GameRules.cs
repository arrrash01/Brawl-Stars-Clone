using UnityEngine;

namespace RiftRumble
{
    public enum BrawlerId { Bront, Vex, Pip }
    public enum GameMode { GemRush, SoloShowdown }
    public enum MatchPhase { Waiting, Countdown, Playing, Finished }

    public readonly struct BrawlerStats
    {
        public readonly string Name, Role, Description;
        public readonly int MaxHealth, Damage;
        public readonly float MoveSpeed, Range, ReloadSeconds, FireInterval, ProjectileSpeed, BlastRadius, FlightDuration, MeleeAngle;
        public readonly Color Color;

        public BrawlerStats(string name, string role, string description, int health, int damage, float speed,
            float range, float reload, float interval, float projectileSpeed, float blastRadius, float flight, float angle, Color color)
        {
            Name = name; Role = role; Description = description; MaxHealth = health; Damage = damage;
            MoveSpeed = speed; Range = range; ReloadSeconds = reload; FireInterval = interval;
            ProjectileSpeed = projectileSpeed; BlastRadius = blastRadius; FlightDuration = flight; MeleeAngle = angle; Color = color;
        }
    }

    public static class GameRules
    {
        public const int TickRate = 30;
        public const float TickDelta = 1f / TickRate;
        public const float PlayerRadius = 0.48f;
        public const int MaxAmmo = 3;
        public const int GemTarget = 10;
        public const float GemHoldSeconds = 15f;
        public const float RoundSeconds = 180f;
        public const byte ProtocolVersion = 3;

        private static readonly BrawlerStats[] Roster =
        {
            new BrawlerStats("BRONT", "CLOSE COMBAT", "An armored bruiser with seismic gauntlets. Get close, hit hard, hold your ground.",
                7200, 1400, 6.2f, 2.9f, 0.9f, 0.4f, 0f, 0f, 0f, 100f, new Color(1f, .32f, .27f)),
            new BrawlerStats("VEX", "STRAIGHT SHOT", "A precise ranger with a pulse carbine. Control long lanes with fast, direct shots.",
                4400, 900, 6f, 12f, 1.1f, .32f, 22f, 0f, 0f, 0f, new Color(.14f, .85f, .97f)),
            new BrawlerStats("PIP", "ARC THROWER", "A little demolition expert. Lob gravity-driven bombs over cover and clear a crowd.",
                4000, 1550, 5.7f, 10.5f, 1.55f, .6f, 12f, 2.6f, .95f, 0f, new Color(1f, .78f, .2f))
        };

        public static BrawlerStats Stats(BrawlerId id) => Roster[Mathf.Clamp((int)id, 0, Roster.Length - 1)];
        public static int Participants(GameMode mode) => mode == GameMode.GemRush ? 6 : 10;
        public static string ModeName(GameMode mode) => mode == GameMode.GemRush ? "GEM RUSH" : "SOLO SHOWDOWN";
        public static string Objective(GameMode mode) => mode == GameMode.GemRush
            ? "3 v 3  •  Collect 10 gems and hold them for 15 seconds."
            : "10 brawlers  •  One life. A closing storm. Last one standing.";
    }
}
