using Unity.Netcode;
using UnityEngine;

namespace RiftRumble
{
    public struct BrawlerInput : INetworkSerializable
    {
        public uint Sequence;
        public uint Epoch;
        public Vector2 Move;
        public Vector3 Aim;
        public byte Buttons;

        public bool Attack => (Buttons & 1) != 0;
        public bool Super => (Buttons & 2) != 0;
        public bool QuickFire => (Buttons & 4) != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Epoch);
            serializer.SerializeValue(ref Move);
            serializer.SerializeValue(ref Aim);
            serializer.SerializeValue(ref Buttons);
        }

        public bool IsFinite => IsValid(Move.x) && IsValid(Move.y)
            && IsValid(Aim.x) && IsValid(Aim.y) && IsValid(Aim.z);

        private static bool IsValid(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Bounded, ordered input history shared by prediction and authoritative replay.</summary>
    public sealed class BrawlerPredictionHistory
    {
        public const int Capacity = 128;
        private readonly BrawlerInput[] _inputs = new BrawlerInput[Capacity];
        private readonly Vector3[] _positions = new Vector3[Capacity];
        private int _head;
        public int Count { get; private set; }

        public void Clear()
        {
            _head = 0;
            Count = 0;
        }

        public void Add(BrawlerInput input, Vector3 predictedPosition)
        {
            if (Count == Capacity)
            {
                _head = (_head + 1) % Capacity;
                Count--;
            }
            _inputs[(_head + Count) % Capacity] = input;
            _positions[(_head + Count) % Capacity] = predictedPosition;
            Count++;
        }

        public bool TryGetPosition(uint sequence, out Vector3 position)
        {
            for (var i = 0; i < Count; i++)
            {
                var index = (_head + i) % Capacity;
                if (_inputs[index].Sequence != sequence) continue;
                position = _positions[index];
                return true;
            }
            position = default;
            return false;
        }

        public void Acknowledge(uint sequence)
        {
            while (Count > 0 && _inputs[_head].Sequence <= sequence)
            {
                _head = (_head + 1) % Capacity;
                Count--;
            }
        }

        public Vector3 Replay(Vector3 authoritativePosition, float speed, GameMode mode)
        {
            var position = authoritativePosition;
            for (var i = 0; i < Count; i++)
            {
                var command = _inputs[(_head + i) % Capacity];
                position = ArenaLayout.Move(position, command.Move, speed, GameRules.TickDelta, mode);
                _positions[(_head + i) % Capacity] = position;
            }
            return position;
        }
    }
}
