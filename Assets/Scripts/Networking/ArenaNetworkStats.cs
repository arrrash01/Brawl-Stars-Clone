using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace RiftRumble
{
    /// <summary>Samples existing reliable-pipeline counters; the server reports the client's upstream loss.</summary>
    sealed class ArenaNetworkStats
    {
        const string MessageName = "RiftRumble.NetworkStats";
        readonly NetworkManager manager;
        readonly UnityTransport transport;
        readonly Dictionary<ulong, ReliableUtility.Statistics> previousSamples = new Dictionary<ulong, ReliableUtility.Statistics>();
        CustomMessagingManager messages;
        NetworkPipeline reliablePipeline;
        float nextSample, lastReport;

        public ulong PingMs { get; private set; }
        public bool HasPing { get; private set; }
        public float IncomingLoss { get; private set; } = -1f;
        public float OutgoingLoss { get; private set; } = -1f;

        public ArenaNetworkStats(NetworkManager networkManager, UnityTransport unityTransport)
        {
            manager = networkManager;
            transport = unityTransport;
        }

        public IDisposable CaptureDriver() => new DriverConstructor(this);

        public void Start()
        {
            Stop();
            messages = manager.CustomMessagingManager;
            messages.RegisterNamedMessageHandler(MessageName, ReceiveReport);
        }

        public void Stop()
        {
            messages?.UnregisterNamedMessageHandler(MessageName);
            messages = null;
            previousSamples.Clear();
            HasPing = false;
            PingMs = 0;
            IncomingLoss = OutgoingLoss = -1f;
            nextSample = lastReport = 0;
        }

        public void Disconnected(ulong clientId) => previousSamples.Remove(clientId);

        public void Update()
        {
            if (!manager.IsListening || manager.ShutdownInProgress)
            {
                if (messages != null) Stop();
                return;
            }
            if (messages == null) return;
            if (manager.IsHost)
            {
                HasPing = true;
                PingMs = 0;
                IncomingLoss = OutgoingLoss = 0;
                return;
            }
            float now = Time.unscaledTime;
            // A lost/stalled report must not leave a reassuring old upstream value on screen.
            if (lastReport == 0 || now - lastReport > 3f) OutgoingLoss = -1f;
            if (now < nextSample) return;
            nextSample = now + 1f;

            if (manager.IsServer)
            {
                using var writer = new FastBufferWriter(sizeof(float), Allocator.Temp);
                foreach (ulong clientId in manager.ConnectedClientsIds)
                {
                    if (clientId == NetworkManager.ServerClientId) continue;
                    float loss = SampleLoss(clientId);
                    writer.Seek(0);
                    writer.WriteValueSafe(loss);
                    messages.SendNamedMessage(MessageName, clientId, writer, NetworkDelivery.Unreliable);
                }
            }
            else if (manager.IsConnectedClient)
            {
                PingMs = transport.GetCurrentRtt(NetworkManager.ServerClientId);
                HasPing = PingMs > 0;
                IncomingLoss = SampleLoss(NetworkManager.ServerClientId);
            }
        }

        float SampleLoss(ulong clientId)
        {
            ref var driver = ref transport.GetNetworkDriver();
            if (!driver.IsCreated) return -1f;
            ulong transportId = manager.GetTransportIdFromClientId(clientId);
            if (transportId == ulong.MaxValue) return -1f;
            // UnityTransport encodes the eight-byte NetworkConnection as its transport ID.
            var id = new NativeArray<ulong>(1, Allocator.Temp);
            id[0] = transportId;
            var connection = id.Reinterpret<NetworkConnection>()[0];
            id.Dispose();
            if (driver.GetConnectionState(connection) != NetworkConnection.State.Connected) return -1f;
            driver.GetPipelineBuffers(reliablePipeline, NetworkPipelineStageId.Get<ReliableSequencedPipelineStage>(),
                connection, out _, out _, out var shared);
            var sample = shared.ReinterpretLoad<ReliableUtility.SharedContext>(0).stats;
            bool hasPrevious = previousSamples.TryGetValue(clientId, out var previous);
            previousSamples[clientId] = sample;
            if (!hasPrevious) return -1f;
            int received = sample.PacketsReceived - previous.PacketsReceived;
            int dropped = sample.PacketsDropped - previous.PacketsDropped;
            // Match UnityTransport's loss estimate, using only this sampling interval.
            return received > 0 ? Mathf.Clamp(100f * dropped / received, 0, 100) : -1f;
        }

        void ReceiveReport(ulong sender, FastBufferReader reader)
        {
            if (manager.IsServer || sender != NetworkManager.ServerClientId || !reader.TryBeginRead(sizeof(float))) return;
            reader.ReadValueSafe(out float loss);
            if (float.IsNaN(loss) || float.IsInfinity(loss) || loss < -1f || loss > 100f) return;
            OutgoingLoss = loss;
            lastReport = Time.unscaledTime;
        }

        sealed class DriverConstructor : INetworkStreamDriverConstructor, IDisposable
        {
            readonly ArenaNetworkStats stats;
            readonly INetworkStreamDriverConstructor previous;

            public DriverConstructor(ArenaNetworkStats networkStats)
            {
                stats = networkStats;
                previous = UnityTransport.s_DriverConstructor;
                UnityTransport.s_DriverConstructor = this;
            }

            public void CreateDriver(UnityTransport owner, out NetworkDriver driver,
                out NetworkPipeline unreliable, out NetworkPipeline sequenced, out NetworkPipeline reliable)
            {
                if (owner == stats.transport) Dispose();
                // Delegate construction unchanged, including the optional server delay simulator.
                (previous ?? owner).CreateDriver(owner, out driver, out unreliable, out sequenced, out reliable);
                if (owner == stats.transport)
                {
                    stats.reliablePipeline = reliable;
                }
            }

            public void Dispose()
            {
                if (ReferenceEquals(UnityTransport.s_DriverConstructor, this))
                    UnityTransport.s_DriverConstructor = previous;
            }
        }
    }
}
