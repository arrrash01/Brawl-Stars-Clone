using System;
using Unity.Netcode;
using UnityEngine;

namespace RiftRumble
{
    public struct ProjectileLaunch : INetworkSerializable, IEquatable<ProjectileLaunch>
    {
        public Vector3 Origin;
        public Vector3 Target;
        public float Speed;
        public float Range;
        public float Duration;
        public float BlastRadius;
        public double StartTime;
        public int Character;
        public bool Lobbed;
        public bool Super;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Origin);
            serializer.SerializeValue(ref Target);
            serializer.SerializeValue(ref Speed);
            serializer.SerializeValue(ref Range);
            serializer.SerializeValue(ref Duration);
            serializer.SerializeValue(ref BlastRadius);
            serializer.SerializeValue(ref StartTime);
            serializer.SerializeValue(ref Character);
            serializer.SerializeValue(ref Lobbed);
            serializer.SerializeValue(ref Super);
        }

        public bool Equals(ProjectileLaunch other) => Origin == other.Origin && Target == other.Target
            && Speed.Equals(other.Speed) && Range.Equals(other.Range) && Duration.Equals(other.Duration)
            && BlastRadius.Equals(other.BlastRadius) && StartTime.Equals(other.StartTime)
            && Character == other.Character && Lobbed == other.Lobbed && Super == other.Super;
    }

    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkProjectile : NetworkBehaviour
    {
        private const float Gravity = 28f;
        private const float ShotRadius = .15f;
        private readonly NetworkVariable<ProjectileLaunch> _launch = new();
        private NetworkBrawler _owner;
        private ArenaMatch _match;
        private int _damage;
        private Vector3 _previous;
        private Material _material;
        private bool _finished;
        private ProjectileLaunch _pendingLaunch;
        private bool _hasPendingLaunch;
        public bool Lobbed => _launch.Value.Lobbed;

        public void ConfigureServer(NetworkBrawler owner, Vector3 origin, Vector3 direction, float speed,
            float range, int damage, bool lobbed, float blastRadius, float flightDuration = .85f, bool super = false)
        {
            _owner = owner;
            _match = owner.Match;
            _match.ConfigureVisibility(NetworkObject);
            _damage = damage;
            direction.y = 0;
            if (direction.sqrMagnitude < .001f) direction = Vector3.forward;
            var target = origin + Vector3.ClampMagnitude(direction, range);
            target.y = lobbed ? .15f : origin.y;
            _pendingLaunch = new ProjectileLaunch
            {
                Origin = origin,
                Target = target,
                Speed = Mathf.Max(1f, speed),
                Range = Mathf.Max(.5f, range),
                Duration = Mathf.Max(.25f, flightDuration),
                BlastRadius = Mathf.Max(0, blastRadius),
                StartTime = owner.NetworkManager.ServerTime.Time,
                Character = owner.Character.Value,
                Lobbed = lobbed,
                Super = super
            };
            _hasPendingLaunch = true;
            transform.position = _previous = origin;
        }

        public override void OnNetworkSpawn()
        {
            // Variables are initialized here; NGO sends their initial spawn state after this callback.
            if (IsServer && _hasPendingLaunch)
            {
                _pendingLaunch.StartTime = NetworkManager.ServerTime.Time;
                _launch.Value = _pendingLaunch;
                _hasPendingLaunch = false;
            }
            _previous = _launch.Value.Origin;
            if (IsServer) NetworkManager.NetworkTickSystem.Tick += TickServer;
            if (IsClient && !Application.isBatchMode) CreateVisual();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null && NetworkManager.NetworkTickSystem != null)
                NetworkManager.NetworkTickSystem.Tick -= TickServer;
            if (_material != null) Destroy(_material);
        }

        private void Update()
        {
            if (!IsSpawned || IsServer) return;
            transform.position = PositionAt(Mathf.Max(0, (float)(NetworkManager.ServerTime.Time - _launch.Value.StartTime)));
            if (_launch.Value.Lobbed) transform.Rotate(200f * Time.deltaTime, 130f * Time.deltaTime, 0, Space.Self);
        }

        // Both the authority and all renderers evaluate the same gravity-driven trajectory.
        private Vector3 PositionAt(float age)
        {
            var launch = _launch.Value;
            if (!launch.Lobbed)
            {
                var direction = (launch.Target - launch.Origin).normalized;
                return launch.Origin + direction * Mathf.Min(launch.Range, age * launch.Speed);
            }
            return LobPosition(launch.Origin, launch.Target, launch.Duration, age);
        }

        public static Vector3 LobPosition(Vector3 origin, Vector3 target, float duration, float age)
        {
            var t = Mathf.Clamp(age, 0, duration);
            var velocity = (target - origin) / duration;
            velocity.y += .5f * Gravity * duration;
            return origin + velocity * t + Vector3.down * (.5f * Gravity * t * t);
        }

        private void TickServer()
        {
            if (_finished || !IsSpawned) return;
            var match = _match;
            if (match == null || !match.IsPlaying || _owner == null || !_owner.IsSpawned)
            {
                NetworkObject.Despawn();
                return;
            }
            var launch = _launch.Value;
            var age = Mathf.Max(0, (float)(NetworkManager.ServerTime.Time - launch.StartTime));
            var next = PositionAt(age);
            if (launch.Lobbed)
            {
                transform.position = next;
                if (age >= launch.Duration) ExplodeServer(launch.Target);
                return;
            }

            var delta = next - _previous;
            var length = delta.magnitude;
            var hitDistance = length;
            NetworkBrawler victim = null;
            var wallHit = false;
            if (length > .0001f)
            {
                var ray = new Ray(_previous, delta / length);
                foreach (var wall in ArenaLayout.Obstacles(match.Mode))
                {
                    // Use shared mathematical bounds, so headless servers need no rendered arena or physics scene.
                    var bounds = new Bounds(wall.Center, wall.Size + Vector3.one * ShotRadius * 2f);
                    if (!bounds.IntersectRay(ray, out var distance) || distance > hitDistance) continue;
                    hitDistance = distance;
                    wallHit = true;
                }
                for (var i = 0; i < match.Players.Count; i++)
                {
                    var target = match.Players[i];
                    if (!_owner.CanDamage(target)) continue;
                    var toTarget = target.ServerPosition - _previous;
                    toTarget.y = 0;
                    var direction = delta / length;
                    var projection = Vector3.Dot(toTarget, direction);
                    var closestSquared = toTarget.sqrMagnitude - projection * projection;
                    var radius = GameRules.PlayerRadius + ShotRadius;
                    if (closestSquared > radius * radius || projection < -radius) continue;
                    var distance = Mathf.Max(0, projection - Mathf.Sqrt(Mathf.Max(0, radius * radius - closestSquared)));
                    if (distance > hitDistance) continue;
                    victim = target;
                    hitDistance = distance;
                    wallHit = false;
                }
            }
            transform.position = _previous + (length > .0001f ? delta / length * hitDistance : Vector3.zero);
            if (victim != null)
            {
                victim.DamageServer(_damage, _owner, !launch.Super);
                FinishServer(transform.position, .65f);
            }
            else if (wallHit || age * launch.Speed >= launch.Range
                     || Mathf.Abs(next.x) > ArenaLayout.HalfWidth || Mathf.Abs(next.z) > ArenaLayout.HalfDepth)
                FinishServer(transform.position, .38f);
            else _previous = next;
        }

        private void ExplodeServer(Vector3 position)
        {
            var match = _match;
            var radius = _launch.Value.BlastRadius;
            for (var i = 0; i < match.Players.Count; i++)
            {
                var target = match.Players[i];
                if (!_owner.CanDamage(target)) continue;
                var delta = target.ServerPosition - position;
                delta.y = 0;
                if (delta.sqrMagnitude <= (radius + GameRules.PlayerRadius) * (radius + GameRules.PlayerRadius))
                    target.DamageServer(_damage, _owner, !_launch.Value.Super);
            }
            FinishServer(position, radius);
        }

        private void FinishServer(Vector3 position, float radius)
        {
            if (_finished) return;
            _finished = true;
            ImpactRpc(position, radius, _launch.Value.Character);
            NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void ImpactRpc(Vector3 position, float radius, int character)
        {
            ArenaAudio.PlayImpact((BrawlerId)character, position);
            if (!Application.isBatchMode) ShowImpact(position, radius, GameRules.Stats((BrawlerId)character).Color);
        }

        private void CreateVisual()
        {
            var shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shot.name = _launch.Value.Lobbed ? "Gravity Bomb" : "Pulse Bolt";
            shot.transform.SetParent(transform, false);
            shot.transform.localScale = Vector3.one * (_launch.Value.Lobbed ? .52f : .27f);
            Destroy(shot.GetComponent<Collider>());
            _material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            var color = GameRules.Stats((BrawlerId)_launch.Value.Character).Color;
            _material.color = color;
            shot.GetComponent<MeshRenderer>().sharedMaterial = _material;
            var trail = shot.AddComponent<TrailRenderer>();
            trail.sharedMaterial = _material;
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0);
            trail.startWidth = _launch.Value.Super ? .32f : .18f;
            trail.endWidth = .02f;
            trail.time = _launch.Value.Lobbed ? .25f : .12f;
            trail.minVertexDistance = .08f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public static void ShowImpact(Vector3 position, float radius, Color color)
        {
            if (Application.isBatchMode) return;
            var effect = new GameObject("Attack pulse");
            effect.transform.position = new Vector3(position.x, .07f, position.z);
            effect.AddComponent<ProjectileImpact>().Configure(radius, color);
        }
    }

    public sealed class ProjectileImpact : MonoBehaviour
    {
        private LineRenderer _line;
        private Material _material;
        private float _age;
        private Color _color;

        public void Configure(float radius, Color color)
        {
            _color = color;
            _material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            _material.color = color;
            _line = gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial = _material;
            _line.useWorldSpace = false;
            _line.loop = true;
            _line.positionCount = 48;
            _line.widthMultiplier = .14f;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (var i = 0; i < 48; i++)
            {
                var angle = i * Mathf.PI * 2 / 48;
                _line.SetPosition(i, new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius);
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;
            var t = Mathf.Clamp01(_age / .32f);
            transform.localScale = Vector3.one * Mathf.Lerp(.55f, 1f, t);
            _line.widthMultiplier = Mathf.Lerp(.16f, .015f, t);
            _line.startColor = _line.endColor = Color.Lerp(_color, Color.clear, t);
            if (_age >= .32f) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
