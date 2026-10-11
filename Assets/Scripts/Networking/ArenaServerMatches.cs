using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RiftRumble
{
    /// <summary>Assigns direct clients to independent arenas on the existing NGO listener.</summary>
    public sealed class ArenaServerMatches
    {
        readonly NetworkManager manager;
        readonly ArenaMatch prefab;
        readonly GameMode? allowedMode;
        readonly float approvalTimeout;
        readonly List<ArenaMatch> matches = new();
        readonly Dictionary<ulong, ArenaMatch> assignments = new();
        readonly Dictionary<ulong, float> pending = new();
        readonly List<ulong> expired = new();
        uint nextRoomId;

        public IReadOnlyList<ArenaMatch> Matches => matches;

        public ArenaServerMatches(NetworkManager manager, ArenaMatch prefab, GameMode? allowedMode)
        {
            this.manager = manager;
            this.prefab = prefab;
            this.allowedMode = allowedMode;
            approvalTimeout = manager.NetworkConfig.ClientConnectionBufferTimeout + 5f;
        }

        public void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            string reason = null;
            if (!ArenaJoinData.TryDecode(request.Payload, out var selection))
                reason = "Incompatible game version or invalid character / username.";
            else if (allowedMode.HasValue && selection.Mode != allowedMode.Value)
                reason = "This server is running a different game mode.";

            response.Approved = reason == null;
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = reason ?? string.Empty;
            if (!response.Approved) return;

            ArenaMatch match = null;
            foreach (var candidate in matches)
                if (candidate != null && candidate.Mode == selection.Mode
                    && candidate.AcceptingPlayers && candidate.ReservedPlayers < candidate.Capacity)
                {
                    match = candidate;
                    break;
                }

            bool created = match == null;
            if (created)
            {
                match = Object.Instantiate(prefab);
                match.name = "Arena Room " + (++nextRoomId);
                match.ConfigureRoom(nextRoomId, selection.Mode);
                matches.Add(match);
            }
            assignments[request.ClientNetworkId] = match;
            pending[request.ClientNetworkId] = Time.realtimeSinceStartup + approvalTimeout;
            // Reserve before spawning/synchronization so the visibility check includes
            // this client, and simultaneous approvals cannot overfill a waiting match.
            match.ReserveClient(request.ClientNetworkId, selection);
            if (created)
            {
                match.NetworkObject.Spawn();
                Debug.Log($"[Rift Rumble] ROOM_CREATED room={match.RoomId} mode={match.Mode}");
            }
            Debug.Log($"[Rift Rumble] ROOM_ASSIGNED client={request.ClientNetworkId} room={match.RoomId} mode={match.Mode}");
        }

        public void ClientConnected(ulong clientId) => pending.Remove(clientId);

        public void ClientDisconnected(ulong clientId)
        {
            pending.Remove(clientId);
            if (assignments.Remove(clientId, out var match) && match != null) match.ReleaseClient(clientId);
        }

        public void Update()
        {
            if (!manager.IsServer) return;
            expired.Clear();
            foreach (var reservation in pending)
                if (Time.realtimeSinceStartup >= reservation.Value) expired.Add(reservation.Key);
            foreach (var clientId in expired)
            {
                manager.DisconnectClient(clientId);
                ClientDisconnected(clientId);
            }
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];
                if (match == null) { matches.RemoveAt(i); continue; }
                if (match.ReservedPlayers != 0 || match.Players.Count != 0) continue;
                Debug.Log($"[Rift Rumble] ROOM_CLOSED room={match.RoomId} mode={match.Mode}");
                matches.RemoveAt(i);
                if (match.IsSpawned) match.NetworkObject.Despawn();
                else Object.Destroy(match.gameObject);
            }
        }
    }
}
