using UnityEngine;
using UnityEngine.Rendering;

namespace RiftRumble
{
    /// <summary>Local-only model rig and feedback; gameplay and hit timing belong to the server.</summary>
    public sealed class BrawlerView : MonoBehaviour
    {
        public Transform ModelRoot { get; private set; }
        public BrawlerId Character { get; private set; }
        Transform torso, head, armLeft, armRight, legLeft, legRight;
        Vector3 torsoRest, headRest;
        Vector3 velocity, aim = Vector3.forward;
        Renderer[] renderers;
        Color[] baseColors;
        MaterialPropertyBlock propertyBlock;
        LineRenderer teamRing;
        Transform shield;
        LineRenderer healingRing;
        Transform[] healingCrosses;
        Material healingMaterial;
        float healRemaining;
        float attackRemaining, attackDuration, hitRemaining, stride, idleOffset;
        bool superAttack, alive = true, flashApplied, protectedFromDamage;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public void Configure(BrawlerId id, int team)
        {
            Character = id;
            if (ModelRoot != null)
            {
                ModelRoot.gameObject.SetActive(false);
                Destroy(ModelRoot.gameObject);
            }
            var prefab = Resources.Load<GameObject>("RiftRumble/Models/" + id);
            if (prefab == null)
            {
                Debug.LogError("Missing Rift Rumble model: " + id + ". Run Rift Rumble/Build Art Assets.", this);
                return;
            }
            ModelRoot = Instantiate(prefab, transform).transform;
            ModelRoot.name = id + " Model";
            ModelRoot.localPosition = Vector3.zero;
            ModelRoot.localRotation = Quaternion.identity;
            ModelRoot.localScale = Vector3.one * .83f;
            aim = transform.forward;
            torso = ModelRoot.Find("Torso");
            head = torso.Find("Head");
            armLeft = torso.Find("ArmL");
            armRight = torso.Find("ArmR");
            legLeft = ModelRoot.Find("LegL");
            legRight = ModelRoot.Find("LegR");
            torsoRest = torso.localPosition;
            headRest = head.localPosition;
            renderers = ModelRoot.GetComponentsInChildren<Renderer>();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                baseColors[i] = renderers[i].sharedMaterial.GetColor(BaseColorId);
            propertyBlock = new MaterialPropertyBlock();
            idleOffset = ((int)id * 2.43f) + Random.value;
            attackRemaining = hitRemaining = 0;
            flashApplied = false;
            alive = true;
            healRemaining = 0;
            SetRing(team);
            if (shield != null)
            {
                shield.gameObject.SetActive(false);
                Destroy(shield.gameObject);
            }
            var shieldPrefab = Resources.Load<GameObject>("RiftRumble/Environment/Shield");
            if (shieldPrefab != null)
            {
                shield = Instantiate(shieldPrefab, transform).transform;
                shield.gameObject.SetActive(false);
            }
        }

        public void SetMotion(Vector3 worldVelocity, Vector3 aimDirection, bool isAlive)
        {
            velocity = worldVelocity;
            velocity.y = 0;
            aimDirection.y = 0;
            if (aimDirection.sqrMagnitude > .001f) aim = aimDirection.normalized;
            alive = isAlive;
        }

        public void PlayAttack(bool isSuper)
        {
            superAttack = isSuper;
            attackDuration = Character == BrawlerId.Bront ? .30f : .21f;
            if (isSuper) attackDuration += .17f;
            attackRemaining = attackDuration;
        }

        public void PlayHit() { hitRemaining = .13f; healRemaining = 0; }
        public void SetProtected(bool value) => protectedFromDamage = value;

        public void PlayHeal()
        {
            if (ModelRoot == null) return;
            if (healingRing == null)
            {
                healingMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                healingMaterial.SetFloat("_Surface", 1);
                healingMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                healingMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                healingMaterial.SetFloat("_ZWrite", 0);
                healingMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                healingMaterial.renderQueue = (int)RenderQueue.Transparent;
                healingRing = HealingLine(transform, "Healing pulse", 40, .065f);
                healingRing.loop = true;
                healingCrosses = new Transform[4];
                for (int i = 0; i < healingCrosses.Length; i++)
                {
                    var cross = new GameObject("Healing cross").transform;
                    cross.SetParent(transform, false);
                    healingCrosses[i] = cross;
                    var horizontal = HealingLine(cross, "Horizontal", 2, .065f);
                    horizontal.SetPosition(0, Vector3.left * .13f); horizontal.SetPosition(1, Vector3.right * .13f);
                    var vertical = HealingLine(cross, "Vertical", 2, .065f);
                    vertical.SetPosition(0, Vector3.down * .13f); vertical.SetPosition(1, Vector3.up * .13f);
                }
            }
            healRemaining = .9f;
        }

        private LineRenderer HealingLine(Transform parent, string name, int count, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.useWorldSpace = false; line.positionCount = count; line.widthMultiplier = width;
            line.sharedMaterial = healingMaterial; line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            return line;
        }

        private void UpdateHealing(float dt)
        {
            if (healingRing == null) return;
            healRemaining = alive ? Mathf.Max(0, healRemaining - dt) : 0;
            bool show = healRemaining > 0;
            healingRing.enabled = show;
            foreach (var cross in healingCrosses) cross.gameObject.SetActive(show);
            if (!show) return;
            float progress = 1 - healRemaining / .9f;
            healingMaterial.SetColor(BaseColorId, new Color(.3f, 1f, .46f, Mathf.Sin(progress * Mathf.PI)));
            for (int i = 0; i < 40; i++)
            {
                float angle = i * Mathf.PI * 2 / 40;
                float radius = .55f + progress * .6f;
                healingRing.SetPosition(i, new Vector3(Mathf.Sin(angle) * radius, .07f, Mathf.Cos(angle) * radius));
            }
            for (int i = 0; i < healingCrosses.Length; i++)
            {
                float angle = i * Mathf.PI * .5f + .4f;
                var cross = healingCrosses[i];
                cross.localPosition = new Vector3(Mathf.Sin(angle) * .7f, .7f + progress * 1.15f + i % 2 * .3f, Mathf.Cos(angle) * .7f);
                if (Camera.main != null) cross.rotation = Camera.main.transform.rotation;
            }
        }

        void LateUpdate()
        {
            if (ModelRoot == null) return;
            float dt = Mathf.Min(Time.deltaTime, .05f);
            UpdateHealing(dt);
            bool visible = alive;
            if (ModelRoot.gameObject.activeSelf != visible) ModelRoot.gameObject.SetActive(visible);
            if (teamRing != null && teamRing.enabled != visible) teamRing.enabled = visible;
            if (shield != null)
            {
                bool showShield = visible && protectedFromDamage;
                if (shield.gameObject.activeSelf != showShield) shield.gameObject.SetActive(showShield);
                if (showShield) shield.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 6f) * .035f);
            }
            if (!visible) return;
            attackRemaining = Mathf.Max(0, attackRemaining - dt);
            hitRemaining = Mathf.Max(0, hitRemaining - dt);
            float speed = Mathf.Clamp01(velocity.magnitude / 5f);
            stride += dt * Mathf.Lerp(2f, 15f, speed);
            float walk = Mathf.Sin(stride) * speed;
            float bob = Mathf.Abs(Mathf.Cos(stride)) * speed * .08f;
            float breathe = Mathf.Sin(Time.time * 2.3f + idleOffset) * .018f;
            float attack = attackRemaining > 0 ? Mathf.Sin((1f - attackRemaining / attackDuration) * Mathf.PI) : 0;
            var targetRotation = Quaternion.LookRotation(aim, Vector3.up);
            ModelRoot.rotation = Quaternion.Slerp(ModelRoot.rotation, targetRotation, 1f - Mathf.Exp(-20f * dt));
            torso.localPosition = torsoRest + new Vector3(0, bob + breathe, attack * -.035f);
            torso.localRotation = Quaternion.Euler(speed * 5f + attack * (superAttack ? -13f : -7f), walk * 4f, -walk * 3f);
            head.localPosition = headRest + Vector3.up * -breathe * .5f;
            head.localRotation = Quaternion.Euler(-speed * 3f, Mathf.Sin(Time.time + idleOffset) * (1 - speed) * 3f, walk * 2f);
            legLeft.localRotation = Quaternion.Euler(walk * 31f, 0, 0);
            legRight.localRotation = Quaternion.Euler(-walk * 31f, 0, 0);
            if (Character == BrawlerId.Bront)
            {
                armLeft.localRotation = Quaternion.Euler(-walk * 20f - attack * 76f, -attack * 23f, -7 - attack * 15f);
                armRight.localRotation = Quaternion.Euler(walk * 20f - attack * 104f, attack * 23f, 7 + attack * 15f);
            }
            else
            {
                armLeft.localRotation = Quaternion.Euler(-walk * 24f - attack * 24f, 0, -8f);
                armRight.localRotation = Quaternion.Euler(-13f + walk * 4f - attack * (Character == BrawlerId.Pip ? 42f : 14f), 0, 8f);
            }
            bool flashing = hitRemaining > 0;
            if (flashing != flashApplied)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    propertyBlock.SetColor(BaseColorId, flashing ? Color.white : baseColors[i]);
                    renderers[i].SetPropertyBlock(propertyBlock);
                }
                flashApplied = flashing;
            }
        }

        void SetRing(int team)
        {
            if (teamRing == null)
            {
                var ring = new GameObject("Team ground marker");
                ring.transform.SetParent(transform, false);
                teamRing = ring.AddComponent<LineRenderer>();
                teamRing.useWorldSpace = false;
                teamRing.loop = true;
                teamRing.positionCount = 40;
                teamRing.widthMultiplier = .065f;
                teamRing.alignment = LineAlignment.View;
                teamRing.shadowCastingMode = ShadowCastingMode.Off;
                teamRing.receiveShadows = false;
                for (int i = 0; i < 40; i++)
                {
                    float angle = i * Mathf.PI * 2f / 40;
                    teamRing.SetPosition(i, new Vector3(Mathf.Sin(angle) * .65f, .045f, Mathf.Cos(angle) * .65f));
                }
            }
            teamRing.sharedMaterial = Resources.Load<Material>("RiftRumble/Materials/" + (team <= 0 ? "BlueTeam" : "RedTeam"));
            teamRing.gameObject.SetActive(team >= 0);
        }

        private void OnDestroy()
        {
            if (healingMaterial != null) Destroy(healingMaterial);
        }
    }
}
