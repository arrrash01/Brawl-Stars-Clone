using System;
using Unity.Collections;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace RiftRumble
{
    /// <summary>Adds optional packet delay to the existing dedicated server transport.</summary>
    sealed class ArenaServerNetworkSimulation : INetworkStreamDriverConstructor, IDisposable
    {
        readonly UnityTransport owner;
        readonly INetworkStreamDriverConstructor previous;
        readonly int pingMs;
        readonly int jitterMs;

        public ArenaServerNetworkSimulation(UnityTransport transport, int ping, int jitter)
        {
            owner = transport;
            pingMs = ping;
            jitterMs = jitter;
            previous = UnityTransport.s_DriverConstructor;
            UnityTransport.s_DriverConstructor = this;
        }

        public void CreateDriver(UnityTransport transport, out NetworkDriver driver,
            out NetworkPipeline unreliableFragmentedPipeline,
            out NetworkPipeline unreliableSequencedFragmentedPipeline,
            out NetworkPipeline reliableSequencedPipeline)
        {
            if (transport != owner)
            {
                (previous ?? transport).CreateDriver(transport, out driver,
                    out unreliableFragmentedPipeline, out unreliableSequencedFragmentedPipeline,
                    out reliableSequencedPipeline);
                return;
            }

            // The hook is global, but only this startup needs it. Later clients/Practice
            // must use their normal driver even when domain reload is disabled.
            Dispose();
            var settings = transport.GetDefaultNetworkSettings();
            try
            {
                // Split added RTT across incoming inputs and outgoing snapshots.
                var delayMs = (pingMs + 1) / 2;
                settings.WithSimulatorStageParameters(maxPacketCount: 400,
                    mode: ApplyMode.AllPackets, packetDelayMs: delayMs, packetJitterMs: jitterMs,
                    randomSeed: (uint)System.Diagnostics.Stopwatch.GetTimestamp());
                driver = transport.UseWebSockets
                    ? NetworkDriver.Create(new WebSocketNetworkInterface(), settings)
                    : NetworkDriver.Create(new UDPNetworkInterface(), settings);
                transport.GetDefaultPipelineConfigurations(out var unreliable, out var sequenced, out var reliable);
                unreliableFragmentedPipeline = CreatePipeline(driver, unreliable);
                unreliableSequencedFragmentedPipeline = CreatePipeline(driver, sequenced);
                reliableSequencedPipeline = CreatePipeline(driver, reliable);
                Debug.Log($"[Rift Rumble] NETWORK_SIMULATION ping={pingMs}ms jitter=+/-{jitterMs}ms " +
                    $"per-direction-delay={delayMs}ms (all clients)");
            }
            finally
            {
                settings.Dispose();
            }
        }

        static NetworkPipeline CreatePipeline(NetworkDriver driver, NativeArray<NetworkPipelineStageId> stages)
        {
            var simulator = NetworkPipelineStageId.Get<SimulatorPipelineStage>();
            foreach (var stage in stages)
                if (stage.Equals(simulator)) return driver.CreatePipeline(stages);

            // Release builds omit the default simulator stage; add it explicitly.
            var simulatedStages = new NativeArray<NetworkPipelineStageId>(stages.Length + 1, Allocator.Temp);
            NativeArray<NetworkPipelineStageId>.Copy(stages, simulatedStages, stages.Length);
            simulatedStages[stages.Length] = simulator;
            var pipeline = driver.CreatePipeline(simulatedStages);
            simulatedStages.Dispose();
            return pipeline;
        }

        public void Dispose()
        {
            if (ReferenceEquals(UnityTransport.s_DriverConstructor, this))
                UnityTransport.s_DriverConstructor = previous;
        }
    }
}
