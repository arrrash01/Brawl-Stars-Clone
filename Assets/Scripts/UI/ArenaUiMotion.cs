using UnityEngine;
using UnityEngine.EventSystems;

namespace RiftRumble
{
    // Cosmetic motion stays local and uses unscaled time while matchmaking is running.
    public sealed class ArenaUiMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private RectTransform rect;
        private Vector2 rest;
        private UnityEngine.UI.Image glow;
        private CanvasGroup group;
        private float delay, elapsed, pulse;
        private bool hovered, pressed, selected;

        public void Configure(UnityEngine.UI.Image highlight, float entranceDelay)
        {
            rect = (RectTransform)transform;
            rest = rect.anchoredPosition;
            glow = highlight;
            delay = entranceDelay;
            group = gameObject.AddComponent<CanvasGroup>();
            Replay();
        }

        public void Select(bool value)
        {
            if (value && !selected) pulse = 1;
            selected = value;
        }

        public void Replay()
        {
            elapsed = 0;
            hovered = pressed = false;
            if (group != null) group.alpha = 0;
        }

        private void Update()
        {
            if (rect == null) return;
            float dt = Time.unscaledDeltaTime;
            elapsed += dt;
            float entrance = Mathf.Clamp01((elapsed - delay) / .38f);
            float ease = 1 - Mathf.Pow(1 - entrance, 3);
            group.alpha = ease;
            group.blocksRaycasts = entrance > .5f;
            pulse = Mathf.MoveTowards(pulse, 0, dt * 3);
            float scale = pressed ? .97f : selected ? 1.025f : hovered ? 1.012f : 1f;
            rect.localScale = Vector3.Lerp(rect.localScale, Vector3.one * (scale + Mathf.Sin(pulse * Mathf.PI) * .025f), 1 - Mathf.Exp(-16 * dt));
            rect.anchoredPosition = rest + Vector2.up * ((selected ? 7 : hovered ? 4 : 0) - (1 - ease) * 32);
            if (glow != null)
            {
                Color color = glow.color;
                color.a = selected ? .24f + Mathf.Sin(Time.unscaledTime * 2.4f) * .06f : hovered ? .15f : .055f;
                glow.color = color;
            }
        }

        public void OnPointerEnter(PointerEventData data) => hovered = true;
        public void OnPointerExit(PointerEventData data) { hovered = pressed = false; }
        public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) pressed = true; }
        public void OnPointerUp(PointerEventData data) => pressed = false;
    }
}
