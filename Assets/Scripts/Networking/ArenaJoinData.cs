using System;
using System.Text;

namespace RiftRumble
{
    /// <summary>A bounded NGO approval payload: version, mode, brawler, name length, UTF-8 name.</summary>
    public readonly struct ArenaJoinData
    {
        public const int MaxUsernameCharacters = 20;
        public const int MaxUsernameBytes = 60;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public readonly GameMode Mode;
        public readonly BrawlerId Brawler;
        public readonly string Username;

        public ArenaJoinData(GameMode mode, BrawlerId brawler, string username)
        {
            Mode = mode;
            Brawler = brawler;
            Username = username;
        }

        public byte[] Encode()
        {
            if (!TryNormalizeUsername(Username, out var name, out var error))
                throw new ArgumentException(error, nameof(Username));
            var nameBytes = Utf8.GetBytes(name);
            var payload = new byte[4 + nameBytes.Length];
            payload[0] = GameRules.ProtocolVersion;
            payload[1] = (byte)Mode;
            payload[2] = (byte)Brawler;
            payload[3] = (byte)nameBytes.Length;
            Buffer.BlockCopy(nameBytes, 0, payload, 4, nameBytes.Length);
            return payload;
        }

        public static bool TryDecode(byte[] payload, out ArenaJoinData data)
        {
            data = default;
            if (payload == null || payload.Length < 5 || payload.Length > 4 + MaxUsernameBytes
                || payload[0] != GameRules.ProtocolVersion || payload[1] > (byte)GameMode.SoloShowdown
                || payload[2] > (byte)BrawlerId.Pip || payload[3] == 0
                || payload.Length != 4 + payload[3]) return false;
            string name;
            try { name = Utf8.GetString(payload, 4, payload[3]); }
            catch (DecoderFallbackException) { return false; }
            if (!TryNormalizeUsername(name, out var normalized, out _)
                || !string.Equals(name, normalized, StringComparison.Ordinal)) return false;
            data = new ArenaJoinData((GameMode)payload[1], (BrawlerId)payload[2], name);
            return true;
        }

        public static bool TryNormalizeUsername(string value, out string username, out string error)
        {
            username = string.Empty;
            error = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Enter a username.";
                return false;
            }
            foreach (var character in value)
                if (char.IsControl(character))
                {
                    error = "Username cannot contain control characters.";
                    return false;
                }
            var trimmed = value.Trim();
            if (trimmed.Length > MaxUsernameCharacters)
            {
                error = "Username must be 20 characters or fewer.";
                return false;
            }
            try
            {
                if (Utf8.GetByteCount(trimmed) > MaxUsernameBytes)
                {
                    error = "Username is too long.";
                    return false;
                }
            }
            catch (EncoderFallbackException)
            {
                error = "Username contains an invalid character.";
                return false;
            }
            username = trimmed;
            return true;
        }
    }
}
