using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RiftRumble
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkBrawler : NetworkBehaviour
    {
        public readonly NetworkVariable<int> Health = new(1);
        public readonly NetworkVariable<int> Gems = new(0);
        public readonly NetworkVariable<int> Team = new(-1);
        public readonly NetworkVariable<int> Power = new(0);
        public readonly NetworkVariable<int> Character = new(0);
        public readonly NetworkVariable<bool> Alive = new(true);
        public readonly NetworkVariable<bool> Invulnerable = new(false);
        public readonly NetworkVariable<float> SuperCharge = new(0);
        public readonly NetworkVariable<int> Ammo = new(GameRules.MaxAmmo);
        private readonly NetworkVariable<bool> _bot = new(false);
        private readonly NetworkVariable<FixedString64Bytes> _displayName = new();
        private readonly NetworkVariable<uint> _spawnEpoch = new(1);
        private readonly NetworkVariable<float> _reloadProgress = new(0);
        private readonly NetworkVariable<NetworkObjectReference> _matchReference = new();
        private ArenaMatch _match;

        public ArenaMatch Match
        {
            get
            {
                if (_match == null && _matchReference.Value.TryGet(out var matchObject))
                    _match = matchObject.GetComponent<ArenaMatch>();
                return _match;
            }
        }

        public static NetworkBrawler Local { get; private set; }
        public static Vector2 TouchMove;
        public static Vector2 TouchAim;
        public static bool TouchAiming;
        public static bool TouchAttack;
        public static bool TouchQuickFire;
        public static bool TouchSuper;
        public static bool InputEnabled = true;

        public bool IsBot => _bot.Value;
        public string DisplayName => _displayName.Value.ToString();
        public BrawlerStats Stats => GameRules.Stats((BrawlerId)Character.Value);
        public int MaxHealth => Stats.MaxHealth + Power.Value * (Stats.MaxHealth / 10);
        public float ReloadProgress => _reloadProgress.Value;
        public Vector3 ServerPosition => _serverPosition;
        public Vector3 AimDirection => _aimDirection;
        public Vector3 Velocity { get; private set; }
        public float LastReconciliationError { get; private set; }
        public int PendingInputs => _history.Count;

        private const int MaxServerQueue = 64;
        private readonly Queue<BrawlerInput> _serverInputs = new(MaxServerQueue);
        private readonly BrawlerPredictionHistory _history = new();
        private BrawlerView _view;
        private Vector3 _serverPosition;
        private Vector3 _remotePosition;
        private Vector3 _aimDirection = Vector3.forward;
        private Vector3 _remoteAim = Vector3.forward;
        private Vector2 _sampledMove;
        private Vector3 _sampledAim;
        private Vector3 _retainedAimOffset;
        private bool _sampledAttack;
        private bool _sampledSuper;
        private uint _localSequence;
        private uint _localEpoch = 1;
        private uint _receivedSequence;
        private uint _acknowledgedSequence;
        private uint _snapshotSerial;
        private uint _lastSnapshot;
        private int _tick;
        private float _fireCooldown;
        private float _reloadTimer;
        private float _quietTime;
        private float _healingTimer;
        private float _spawnProtection;
        private LineRenderer _aimGuide;
        private LineRenderer _landingGuide;
        private Material _aimMaterial;
        private bool _hasSpawnConfiguration;
        private int _configuredCharacter, _configuredTeam;
        private bool _configuredBot;
        private FixedString64Bytes _configuredName;
        private struct ReleasedShot
        {
            public Vector3 AimOffset;
            public bool QuickFire;
        }
        private readonly Queue<ReleasedShot> _releasedShots = new(4);
        private readonly List<RaycastResult> _uiHits = new(8);
        private PointerEventData _pointerData;
        private EventSystem _pointerEventSystem;
        private bool _mouseAiming, _triggerArmed;
        private Vector2 _mousePressPosition;
        private Vector3 _mouseFallbackAimOffset, _triggerFallbackAimOffset;
        private uint _manualAimSequence, _lastAutoAimSequence;
        private float _mousePressTime;
        private bool _mouseDeliberateAim, _triggerDeliberateAim;
        private bool _waitForMouseUp = true, _waitForTriggerUp = true;
        private bool _hasFocus = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            TouchMove = TouchAim = Vector2.zero;
            TouchAiming = TouchAttack = TouchQuickFire = TouchSuper = false;
            InputEnabled = true;
        }

        public void ConfigureServer(ArenaMatch match, BrawlerId id, int team, bool bot, string displayName, Vector3 position)
        {
            _match = match;
            _configuredCharacter = Mathf.Clamp((int)id, 0, 2);
            _configuredTeam = team;
            _configuredBot = bot;
            // FixedString64 holds 61 UTF-8 bytes; short ASCII fallback also handles oversized names.
            var safeName = string.IsNullOrWhiteSpace(displayName) ? "Brawler" : displayName;
            if (System.Text.Encoding.UTF8.GetByteCount(safeName) > 60) safeName = "Brawler";
            _configuredName = new FixedString64Bytes(safeName);
            _hasSpawnConfiguration = true;
            _serverPosition = _remotePosition = position;
            transform.position = position;
            _sampledAim = position + Vector3.forward * GameRules.Stats((BrawlerId)_configuredCharacter).Range;
        }

        public override void OnNetworkSpawn()
        {
            // NGO initializes variables before this callback and serializes the spawn after it returns.
            // Apply pending authority data before model, bot, or ownership-dependent initialization.
            if (IsServer && _hasSpawnConfiguration)
            {
                _matchReference.Value = new NetworkObjectReference(_match.NetworkObject);
                Character.Value = _configuredCharacter;
                Team.Value = _configuredTeam;
                _bot.Value = _configuredBot;
                _displayName.Value = _configuredName;
                Health.Value = MaxHealth;
                Alive.Value = true;
                Ammo.Value = GameRules.MaxAmmo;
                _hasSpawnConfiguration = false;
            }
            if (IsClient && !Application.isBatchMode) _view = GetComponentInChildren<BrawlerView>();
            _view?.Configure((BrawlerId)Character.Value, Team.Value);
            _serverPosition = _remotePosition = transform.position;
            _retainedAimOffset = Vector3.forward * Stats.Range;
            _sampledAim = transform.position + _retainedAimOffset;
            _localEpoch = _spawnEpoch.Value;
            _spawnProtection = 1.2f;
            if (IsServer) Invulnerable.Value = true;
            Character.OnValueChanged += OnCharacterChanged;
            Team.OnValueChanged += OnTeamChanged;
            Health.OnValueChanged += OnHealthChanged;
            Alive.OnValueChanged += OnAliveChanged;
            NetworkManager.NetworkTickSystem.Tick += NetworkTick;
            Match?.Register(this);
            if (IsOwner && !IsBot)
            {
                Local = this;
                _hasFocus = Application.isFocused;
                CancelLocalInput();
                if (!Application.isBatchMode) CreateAimGuide();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
                NetworkManager.NetworkTickSystem.Tick -= NetworkTick;
            Character.OnValueChanged -= OnCharacterChanged;
            Team.OnValueChanged -= OnTeamChanged;
            Health.OnValueChanged -= OnHealthChanged;
            Alive.OnValueChanged -= OnAliveChanged;
            Match?.Unregister(this);
            if (Local == this) { CancelLocalInput(); Local = null; }
            _history.Clear();
            _serverInputs.Clear();
            if (_aimGuide != null) Destroy(_aimGuide.gameObject);
            if (_landingGuide != null) Destroy(_landingGuide.gameObject);
            if (_aimMaterial != null) Destroy(_aimMaterial);
        }

        private void OnCharacterChanged(int previous, int current) => _view?.Configure((BrawlerId)current, Team.Value);
        private void OnTeamChanged(int previous, int current) => _view?.Configure((BrawlerId)Character.Value, current);
        private void OnAliveChanged(bool previous, bool current)
        {
            if (!current && Local == this) CancelLocalInput();
        }
        private void OnHealthChanged(int previous, int current)
        {
            if (current < previous)
            {
                _view?.PlayHit();
                ArenaAudio.PlayHit(transform.position);
            }
            else if (current > previous && Alive.Value && !Invulnerable.Value)
                _view?.PlayHeal();
        }

        private void Update()
        {
            if (!IsSpawned || !IsClient) return;
            if (IsOwner && !IsBot) SampleInput();
            if (!IsServer && (!IsOwner || IsBot))
            {
                var previous = transform.position;
                transform.position = Vector3.Lerp(previous, _remotePosition, 1f - Mathf.Exp(-18f * Time.deltaTime));
                Velocity = (transform.position - previous) / Mathf.Max(Time.deltaTime, .0001f);
                _aimDirection = Vector3.Slerp(_aimDirection, _remoteAim, 1f - Mathf.Exp(-20f * Time.deltaTime));
            }
            _view?.SetMotion(Velocity, _aimDirection, Alive.Value);
            _view?.SetProtected(Invulnerable.Value);
            if (_aimGuide != null) UpdateAimGuide();
        }

        private void CreateAimGuide()
        {
            _aimMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            _aimMaterial.color = Color.Lerp(Stats.Color, Color.white, .65f);
            _aimGuide = CreateGuideLine("Aim trajectory");
            _landingGuide = CreateGuideLine("Lob landing radius");
            _landingGuide.loop = true;
        }

        private LineRenderer CreateGuideLine(string label)
        {
            var guide = new GameObject(label).AddComponent<LineRenderer>();
            guide.transform.SetParent(transform, false);
            guide.sharedMaterial = _aimMaterial;
            guide.useWorldSpace = true;
            guide.widthMultiplier = .045f;
            guide.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            guide.receiveShadows = false;
            return guide;
        }

        private void UpdateAimGuide()
        {
            var visible = Alive.Value && InputEnabled && Match != null && Match.IsPlaying;
            _aimGuide.enabled = visible;
            _landingGuide.enabled = visible && (BrawlerId)Character.Value == BrawlerId.Pip;
            if (!visible) return;
            var origin = transform.position + Vector3.up * .08f;
            var character = (BrawlerId)Character.Value;
            var super = _sampledSuper && SuperCharge.Value >= .999f;
            if (character == BrawlerId.Bront)
            {
                _aimGuide.positionCount = 27;
                _aimGuide.SetPosition(0, origin);
                var angle = super ? 360 : Stats.MeleeAngle;
                var range = super ? 4.8f : Stats.Range;
                for (var i = 0; i <= 24; i++)
                    _aimGuide.SetPosition(i + 1, origin + Quaternion.Euler(0, -angle * .5f + i * angle / 24, 0) * _aimDirection * range);
                _aimGuide.SetPosition(26, origin);
            }
            else if (character == BrawlerId.Vex)
            {
                _aimGuide.positionCount = 2;
                _aimGuide.SetPosition(0, origin);
                _aimGuide.SetPosition(1, origin + _aimDirection * Stats.Range);
            }
            else
            {
                var target = transform.position + _retainedAimOffset;
                target.y = .15f;
                _aimGuide.positionCount = 25;
                for (var i = 0; i < 25; i++)
                    _aimGuide.SetPosition(i, NetworkProjectile.LobPosition(transform.position + Vector3.up * .8f,
                        target, Stats.FlightDuration, i * Stats.FlightDuration / 24));
                _landingGuide.positionCount = 40;
                var radius = Stats.BlastRadius * (super ? 1.6f : 1f);
                for (var i = 0; i < 40; i++)
                {
                    var angle = i * Mathf.PI * 2 / 40;
                    _landingGuide.SetPosition(i, new Vector3(target.x + Mathf.Sin(angle) * radius, .08f, target.z + Mathf.Cos(angle) * radius));
                }
            }
        }

        private void SampleInput()
        {
            _sampledMove = Vector2.zero;
            _sampledAttack = _sampledSuper = false;
            if (!CanSampleLocalInput()) { CancelLocalInput(); return; }
            _sampledAim = transform.position + _retainedAimOffset;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                _sampledMove.x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                    - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
                _sampledMove.y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                    - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
                _sampledSuper = keyboard.spaceKey.isPressed;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                var pointer = mouse.position.ReadValue();
                var overUi = PointerOverUi(pointer);
                var touchActive = TouchAiming || TouchAttack || HasTouchActivity();
                if (!mouse.leftButton.isPressed) _waitForMouseUp = false;
                if (touchActive)
                {
                    _mouseAiming = false;
                    _waitForMouseUp = true;
                }
                else
                {
                    if (!_waitForMouseUp && mouse.leftButton.wasPressedThisFrame && !overUi)
                    {
                        _mouseAiming = true;
                        _mousePressPosition = pointer;
                        _mousePressTime = Time.unscaledTime;
                        _mouseFallbackAimOffset = _retainedAimOffset;
                        _mouseDeliberateAim = false;
                    }
                    if (_mouseAiming && ((pointer - _mousePressPosition).sqrMagnitude >= 144f
                        || Time.unscaledTime - _mousePressTime >= .18f)) _mouseDeliberateAim = true;
                    if (_mouseAiming && !overUi && (mouse.leftButton.isPressed || mouse.leftButton.wasReleasedThisFrame))
                        SampleMouseAim(pointer);
                    if (mouse.leftButton.wasReleasedThisFrame)
                    {
                        if (_mouseAiming && !overUi)
                        {
                            if (!_mouseDeliberateAim) SetRetainedAim(_mouseFallbackAimOffset);
                            QueueReleasedShot(!_mouseDeliberateAim);
                        }
                        _mouseAiming = false;
                    }
                    if (!overUi) _sampledSuper |= mouse.rightButton.isPressed;
                }
            }
            else _mouseAiming = false;

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.leftStick.ReadValue().sqrMagnitude > .04f) _sampledMove = gamepad.leftStick.ReadValue();
                var stick = gamepad.rightStick.ReadValue();
                if (!gamepad.rightTrigger.isPressed) _waitForTriggerUp = false;
                if (!_waitForTriggerUp && gamepad.rightTrigger.wasPressedThisFrame)
                {
                    _triggerArmed = true;
                    _triggerDeliberateAim = false;
                    _triggerFallbackAimOffset = _retainedAimOffset;
                }
                if (stick.sqrMagnitude > .04f)
                {
                    CaptureAim(transform.position + new Vector3(stick.x, 0, stick.y) * Stats.Range);
                    if (_triggerArmed) _triggerDeliberateAim = true;
                }
                if (gamepad.rightTrigger.wasReleasedThisFrame)
                {
                    if (_triggerArmed)
                    {
                        if (!_triggerDeliberateAim) SetRetainedAim(_triggerFallbackAimOffset);
                        QueueReleasedShot(!_triggerDeliberateAim);
                    }
                    _triggerArmed = false;
                }
                _sampledSuper |= gamepad.leftTrigger.isPressed;
            }
            else _triggerArmed = false;

            if (TouchMove.sqrMagnitude > .001f) _sampledMove = TouchMove;
            if (!TouchQuickFire && (TouchAiming || TouchAttack) && TouchAim.sqrMagnitude > .001f)
                CaptureAim(transform.position + new Vector3(TouchAim.x, 0, TouchAim.y) * Stats.Range);
            if (TouchAttack)
            {
                QueueReleasedShot(TouchQuickFire);
                TouchAttack = TouchQuickFire = false;
            }
            _sampledSuper |= TouchSuper;
            _sampledMove = Vector2.ClampMagnitude(_sampledMove, 1f);
            _aimDirection = _retainedAimOffset.normalized;
        }

        private bool CanSampleLocalInput() => _hasFocus && InputEnabled && Alive.Value
            && Match != null && Match.IsPlaying;

        private void CaptureAim(Vector3 point)
        {
            SetRetainedAim(point - transform.position);
            // An older quick-fire confirmation must not replace a newer manual gesture.
            _manualAimSequence = _localSequence + 1;
        }

        private void SetRetainedAim(Vector3 offset)
        {
            offset.y = 0;
            offset = Vector3.ClampMagnitude(offset, Stats.Range);
            if (offset.sqrMagnitude < .25f) offset = _aimDirection * .5f;
            _retainedAimOffset = offset;
            _sampledAim = transform.position + offset;
            _sampledAim.y = 0;
            _aimDirection = offset.normalized;
        }

        private void SampleMouseAim(Vector2 pointer)
        {
            var camera = Camera.main;
            if (camera == null) return;
            var ray = camera.ScreenPointToRay(pointer);
            if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var distance)) CaptureAim(ray.GetPoint(distance));
        }

        private bool PointerOverUi(Vector2 pointer)
        {
            var events = EventSystem.current;
            if (events == null) return false;
            if (_pointerData == null || _pointerEventSystem != events)
            {
                _pointerEventSystem = events;
                _pointerData = new PointerEventData(events);
            }
            _pointerData.Reset();
            _pointerData.position = pointer;
            _uiHits.Clear();
            events.RaycastAll(_pointerData, _uiHits);
            return _uiHits.Count > 0;
        }

        private static bool HasTouchActivity()
        {
            var screen = Touchscreen.current;
            if (screen == null) return false;
            foreach (var touch in screen.touches)
                if (touch.press.isPressed || touch.press.wasPressedThisFrame || touch.press.wasReleasedThisFrame) return true;
            return false;
        }

        private void QueueReleasedShot(bool quickFire)
        {
            // Preserve the released direction and distance through movement and later gestures.
            if (_releasedShots.Count < 4) _releasedShots.Enqueue(new ReleasedShot { AimOffset = _retainedAimOffset, QuickFire = quickFire });
        }

        private void CancelLocalInput()
        {
            _releasedShots.Clear();
            _mouseAiming = _triggerArmed = false;
            _waitForMouseUp = _waitForTriggerUp = true;
            _sampledMove = Vector2.zero;
            _sampledAttack = _sampledSuper = false;
            if (Local != this) return;
            TouchMove = TouchAim = Vector2.zero;
            TouchAiming = TouchAttack = TouchQuickFire = TouchSuper = false;
        }

        private void OnApplicationFocus(bool focused)
        {
            _hasFocus = focused;
            if (!focused && Local == this) CancelLocalInput();
        }

        private void OnDisable()
        {
            if (Local == this) CancelLocalInput();
        }

        public void SetBotInput(Vector2 move, Vector3 aimPoint, bool attack, bool super)
        {
            if (!IsServer || !IsBot) return;
            _sampledMove = Vector2.ClampMagnitude(move, 1f);
            _sampledAim = aimPoint;
            _sampledAttack = attack;
            _sampledSuper = super;
        }

        private BrawlerInput CreateInput()
        {
            var attack = _sampledAttack;
            var aim = IsBot ? _sampledAim : transform.position + _retainedAimOffset;
            var quickFire = false;
            if (!IsBot)
            {
                attack = _releasedShots.Count > 0;
                if (attack)
                {
                    var shot = _releasedShots.Dequeue();
                    aim = transform.position + shot.AimOffset;
                    quickFire = shot.QuickFire;
                }
            }
            return new BrawlerInput
            {
                Sequence = ++_localSequence,
                Epoch = _localEpoch,
                Move = _sampledMove,
                Aim = aim,
                Buttons = (byte)((attack ? 1 : 0) | (_sampledSuper ? 2 : 0) | (quickFire ? 4 : 0))
            };
        }

        private void NetworkTick()
        {
            if (!IsSpawned || Match == null) return;
            var match = Match;
            // A full history means acknowledgements have stopped: stop predicting until recovered.
            if (IsOwner && !IsBot && (IsServer || _history.Count < BrawlerPredictionHistory.Capacity - 2))
            {
                if (!CanSampleLocalInput()) CancelLocalInput();
                var input = CreateInput();
                if (IsServer) EnqueueInput(input);
                else
                {
                    var previous = transform.position;
                    if (Alive.Value && match.IsPlaying)
                        transform.position = ArenaLayout.Move(previous, input.Move, Stats.MoveSpeed, GameRules.TickDelta, match.Mode);
                    Velocity = (transform.position - previous) / GameRules.TickDelta;
                    _history.Add(input, transform.position);
                    SubmitInputRpc(input);
                }
            }

            if (!IsServer) return;
            TickServerTimers();
            BrawlerInput command = default;
            var hasInput = false;
            if (IsBot)
            {
                command = CreateInput();
                hasInput = true;
            }
            else if (_serverInputs.Count > 0)
            {
                // A lag burst must not grant additional simulation time or leave seconds of stale input.
                TrimStaleMovementInputs(6);
                command = _serverInputs.Dequeue();
                hasInput = true;
            }

            Velocity = Vector3.zero;
            if (hasInput)
            {
                _acknowledgedSequence = command.Sequence;
                if (Alive.Value && match.IsPlaying)
                {
                    var previous = _serverPosition;
                    _serverPosition = ArenaLayout.Move(previous, command.Move, Stats.MoveSpeed, GameRules.TickDelta, match.Mode);
                    transform.position = _serverPosition;
                    Velocity = (_serverPosition - previous) / GameRules.TickDelta;
                    // Quick-fire keeps its retained offset when this input also moves the player.
                    if (command.QuickFire) command.Aim += _serverPosition - previous;
                    var aim = command.Aim - _serverPosition;
                    aim.y = 0;
                    if (aim.sqrMagnitude > .001f) _aimDirection = aim.normalized;
                    if (command.Super && SuperCharge.Value >= .999f) AttackServer(command.Aim, true);
                    else if (command.Attack) AttackServer(command.Aim, false, command.QuickFire, command.Sequence);
                }
            }

            if (++_tick % 2 == 0)
            {
                _reloadProgress.Value = Ammo.Value >= GameRules.MaxAmmo ? 0f : Mathf.Clamp01(_reloadTimer / Stats.ReloadSeconds);
                SnapshotRpc(_serverPosition, _aimDirection, _acknowledgedSequence, _spawnEpoch.Value, ++_snapshotSerial);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitInputRpc(BrawlerInput input, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || IsBot) return;
            EnqueueInput(input);
        }

        private void EnqueueInput(BrawlerInput input)
        {
            if (input.Epoch != _spawnEpoch.Value || !input.IsFinite || input.Sequence <= _receivedSequence) return;
            if (input.Sequence - _receivedSequence > BrawlerPredictionHistory.Capacity) return;
            input.Move = Vector2.ClampMagnitude(input.Move, 1f);
            input.Aim.y = 0;
            input.Aim.x = Mathf.Clamp(input.Aim.x, -1000f, 1000f);
            input.Aim.z = Mathf.Clamp(input.Aim.z, -1000f, 1000f);
            _receivedSequence = input.Sequence;
            if (_serverInputs.Count == MaxServerQueue) TrimStaleMovementInputs(MaxServerQueue - 1);
            // Preserve accepted releases if a flood fills every slot with actions. Never grow the queue.
            if (_serverInputs.Count == MaxServerQueue) return;
            _serverInputs.Enqueue(input);
        }

        private void TrimStaleMovementInputs(int targetCount)
        {
            var discard = _serverInputs.Count - targetCount;
            if (discard <= 0) return;
            var originalCount = _serverInputs.Count;
            for (var i = 0; i < originalCount; i++)
            {
                var input = _serverInputs.Dequeue();
                if (discard > 0 && !input.Attack && !input.Super) discard--;
                else _serverInputs.Enqueue(input);
            }
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server, Delivery = RpcDelivery.Unreliable)]
        private void SnapshotRpc(Vector3 position, Vector3 aim, uint acknowledged, uint epoch, uint serial)
        {
            if (IsServer || epoch < _localEpoch || serial <= _lastSnapshot) return;
            _lastSnapshot = serial;
            if (epoch > _localEpoch) ApplyTeleport(position, epoch);
            _remotePosition = position;
            _remoteAim = aim;
            if (!IsOwner || IsBot) return;

            LastReconciliationError = _history.TryGetPosition(acknowledged, out var predicted)
                ? Vector3.Distance(predicted, position) : 0f;
            _history.Acknowledge(acknowledged);
            // Restore the authoritative state, then replay every unacknowledged input with the same integrator.
            transform.position = Alive.Value && Match != null && Match.IsPlaying
                ? _history.Replay(position, Stats.MoveSpeed, Match.Mode)
                : position;
        }

        public void RespawnServer(Vector3 position)
        {
            if (!IsServer) return;
            _spawnEpoch.Value++;
            _serverInputs.Clear();
            _receivedSequence = _acknowledgedSequence = 0;
            _localSequence = 0;
            _localEpoch = _spawnEpoch.Value;
            _history.Clear();
            _serverPosition = _remotePosition = position;
            transform.position = position;
            Velocity = Vector3.zero;
            // Bots are refreshed less often than simulation: never reuse an attack from their previous life.
            _sampledMove = Vector2.zero;
            _retainedAimOffset = _aimDirection * Stats.Range;
            _sampledAim = position + _retainedAimOffset;
            _manualAimSequence = _lastAutoAimSequence = 0;
            _sampledAttack = _sampledSuper = false;
            _releasedShots.Clear();
            if (Local == this) CancelLocalInput();
            Health.Value = MaxHealth;
            Alive.Value = true;
            Ammo.Value = GameRules.MaxAmmo;
            _reloadTimer = _reloadProgress.Value = _fireCooldown = _quietTime = _healingTimer = 0;
            _spawnProtection = 1.5f;
            Invulnerable.Value = true;
            TeleportRpc(position, _spawnEpoch.Value);
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void TeleportRpc(Vector3 position, uint epoch)
        {
            if (!IsServer && epoch > _localEpoch) ApplyTeleport(position, epoch);
        }

        private void ApplyTeleport(Vector3 position, uint epoch)
        {
            _localEpoch = epoch;
            _localSequence = 0;
            _history.Clear();
            transform.position = _remotePosition = position;
            _retainedAimOffset = _aimDirection * Stats.Range;
            _sampledAim = position + _retainedAimOffset;
            _manualAimSequence = _lastAutoAimSequence = 0;
            _sampledMove = Vector2.zero;
            _sampledAttack = _sampledSuper = false;
            _releasedShots.Clear();
            if (Local == this) CancelLocalInput();
            Velocity = Vector3.zero;
        }

        private void TickServerTimers()
        {
            var dt = GameRules.TickDelta;
            _fireCooldown = Mathf.Max(0, _fireCooldown - dt);
            _spawnProtection = Mathf.Max(0, _spawnProtection - dt);
            if (Invulnerable.Value && _spawnProtection <= 0) Invulnerable.Value = false;
            if (!Alive.Value || Match == null || !Match.IsPlaying) return;
            if (Ammo.Value < GameRules.MaxAmmo)
            {
                _reloadTimer += dt;
                if (_reloadTimer >= Stats.ReloadSeconds)
                {
                    Ammo.Value++;
                    _reloadTimer -= Stats.ReloadSeconds;
                }
            }
            _quietTime += dt;
            if (_quietTime < 3f || Health.Value >= MaxHealth)
            {
                _healingTimer = 0;
                return;
            }
            _healingTimer += dt;
            if (_healingTimer >= 1f)
            {
                _healingTimer -= 1f;
                Health.Value = Mathf.Min(MaxHealth, Health.Value + Mathf.CeilToInt(MaxHealth * .13f));
            }
        }

        private void AttackServer(Vector3 aimPoint, bool super, bool quickFire = false, uint sequence = 0)
        {
            if (_fireCooldown > .0001f || (!super && Ammo.Value <= 0)) return;
            if (quickFire)
            {
                var nearestSquared = Stats.Range * Stats.Range;
                var players = Match.Players;
                for (var i = 0; i < players.Count; i++)
                {
                    var target = players[i];
                    if (!CanDamage(target) || target.Team.Value == Team.Value) continue;
                    var offset = target.ServerPosition - _serverPosition;
                    offset.y = 0;
                    if (offset.sqrMagnitude > nearestSquared) continue;
                    nearestSquared = offset.sqrMagnitude;
                    aimPoint = target.ServerPosition;
                }
                var direction = aimPoint - _serverPosition;
                direction.y = 0;
                if (direction.sqrMagnitude > .001f) _aimDirection = direction.normalized;
                var confirmedOffset = Vector3.ClampMagnitude(direction, Stats.Range);
                if (confirmedOffset.sqrMagnitude < .25f) confirmedOffset = _aimDirection * .5f;
                QuickFireAimRpc(confirmedOffset, sequence, _spawnEpoch.Value);
            }
            if (super) SuperCharge.Value = 0;
            else Ammo.Value--;
            _reloadTimer = _quietTime = _healingTimer = 0;
            _fireCooldown = Stats.FireInterval;
            _spawnProtection = 0;
            Invulnerable.Value = false;
            var damage = Mathf.RoundToInt(Stats.Damage * (1f + .1f * Power.Value) * (super ? 1.5f : 1f));
            var character = (BrawlerId)Character.Value;
            if (character == BrawlerId.Bront)
            {
                var radius = super ? 4.8f : Stats.Range;
                var minimumDot = super ? -1f : Mathf.Cos(Stats.MeleeAngle * .5f * Mathf.Deg2Rad);
                var players = Match.Players;
                for (var i = 0; i < players.Count; i++)
                {
                    var target = players[i];
                    if (!CanDamage(target)) continue;
                    var delta = target.ServerPosition - _serverPosition;
                    delta.y = 0;
                    if (delta.sqrMagnitude > (radius + GameRules.PlayerRadius) * (radius + GameRules.PlayerRadius)) continue;
                    if (delta.sqrMagnitude > .001f && Vector3.Dot(delta.normalized, _aimDirection) < minimumDot) continue;
                    if (!ArenaLayout.HasLineOfSight(_serverPosition, target.ServerPosition, Match.Mode)) continue;
                    target.DamageServer(damage, this, !super);
                }
            }
            else if (Match.ProjectilePrefab != null)
            {
                if (character == BrawlerId.Vex && super)
                {
                    for (var i = -1; i <= 1; i++)
                        SpawnProjectile(Quaternion.Euler(0, i * 9f, 0) * _aimDirection * Stats.Range, damage, false, true);
                }
                else
                {
                    var target = Vector3.ClampMagnitude(aimPoint - _serverPosition, Stats.Range);
                    target.y = 0;
                    if (target.sqrMagnitude < .25f) target = _aimDirection * .5f;
                    SpawnProjectile(character == BrawlerId.Vex ? _aimDirection * Stats.Range : target,
                        damage, character == BrawlerId.Pip, super);
                }
            }
            AttackEffectRpc(super);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void QuickFireAimRpc(Vector3 offset, uint sequence, uint epoch)
        {
            if (!IsOwner || IsBot || !Alive.Value || epoch != _localEpoch
                || sequence < _manualAimSequence || sequence <= _lastAutoAimSequence
                || _mouseAiming || _triggerArmed || TouchAiming) return;
            _lastAutoAimSequence = sequence;
            SetRetainedAim(offset);
        }

        private void SpawnProjectile(Vector3 targetOffset, int damage, bool lobbed, bool super)
        {
            var shot = Instantiate(Match.ProjectilePrefab, _serverPosition + Vector3.up * .8f, Quaternion.identity);
            shot.ConfigureServer(this, _serverPosition + Vector3.up * .8f, targetOffset,
                Stats.ProjectileSpeed, Stats.Range, damage, lobbed,
                super ? Stats.BlastRadius * 1.6f : Stats.BlastRadius, Stats.FlightDuration, super);
            shot.NetworkObject.Spawn();
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void AttackEffectRpc(bool super)
        {
            _view?.PlayAttack(super);
            ArenaAudio.PlayShot((BrawlerId)Character.Value, transform.position, super);
            if ((BrawlerId)Character.Value == BrawlerId.Bront)
                NetworkProjectile.ShowImpact(transform.position + _aimDirection * (super ? 0 : .7f),
                    super ? 4.8f : Stats.Range * .65f, Stats.Color);
        }

        public bool CanDamage(NetworkBrawler target)
        {
            return target != null && target != this && target.Alive.Value
                && Match != null && target.Match == Match
                && (Team.Value < 0 || Team.Value != target.Team.Value);
        }

        public void DamageServer(int amount, NetworkBrawler attacker, bool chargeSuper = true)
        {
            if (!IsServer || !Alive.Value || amount <= 0 || _spawnProtection > 0
                || Match == null || !Match.IsPlaying) return;
            if (attacker != null && !attacker.CanDamage(this)) return;
            var appliedDamage = Mathf.Min(Health.Value, amount);
            Health.Value -= appliedDamage;
            _quietTime = _healingTimer = 0;
            if (attacker != null && chargeSuper)
                attacker.SuperCharge.Value = Mathf.Clamp01(attacker.SuperCharge.Value + .24f * appliedDamage / attacker.Stats.Damage);
            if (Health.Value > 0) return;
            Alive.Value = false;
            Velocity = Vector3.zero;
            _serverInputs.Clear();
            Match.OnEliminated(this, attacker);
        }
    }
}
