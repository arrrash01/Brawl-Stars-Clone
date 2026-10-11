using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RiftRumble
{
    public sealed class ArenaTouchStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, ICanvasRaycastFilter
    {
        private RectTransform handle, visual, companion;
        private CanvasGroup visualGroup;
        private bool attack;
        private bool aimedThisGesture;
        private Vector2 pressPosition, lastValue, home;
        private float pressedAt;
        private Vector2 areaSize;
        private int pointer = int.MinValue;
        public void Configure(RectTransform baseVisual, RectTransform knob, bool isAttack, RectTransform superButton = null)
        {
            visual = baseVisual; handle = knob; attack = isAttack; companion = superButton;
            visualGroup = visual.gameObject.AddComponent<CanvasGroup>();
            Canvas.ForceUpdateCanvases();
            UpdateHome();
            ResetStick(false);
        }
        private void UpdateHome()
        {
            var area = (RectTransform)transform;
            areaSize = area.rect.size;
            home = new Vector2(attack ? area.rect.xMax - 200 : area.rect.xMin + 200, area.rect.yMin + 200);
        }
        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            // Invisible touch zones must not swallow desktop aiming clicks on the arena.
            if (Application.isMobilePlatform) return true;
            if (Touchscreen.current != null)
                foreach (var contact in Touchscreen.current.touches)
                    if (contact.isInProgress) return true;
            return visual != null && RectTransformUtility.RectangleContainsScreenPoint(visual, screenPoint, eventCamera);
        }
        public void OnPointerDown(PointerEventData data)
        {
            if (pointer != int.MinValue || data.button != PointerEventData.InputButton.Left
                || !NetworkBrawler.InputEnabled || NetworkBrawler.Local == null || !NetworkBrawler.Local.Alive.Value) return;
            aimedThisGesture = false;
            pressPosition = data.position; pressedAt = Time.unscaledTime;
            pointer = data.pointerId;
            var area = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, data.position, data.pressEventCamera, out Vector2 point);
            float radius = visual.sizeDelta.x * .5f;
            visual.anchoredPosition = new Vector2(Mathf.Clamp(point.x, area.rect.xMin + radius, area.rect.xMax - radius),
                Mathf.Clamp(point.y, area.rect.yMin + radius, area.rect.yMax - radius));
            visualGroup.alpha = 1;
            PlaceCompanion();
            OnDrag(data);
        }
        public void OnDrag(PointerEventData data)
        {
            if (data.pointerId != pointer) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(visual, data.position, data.pressEventCamera, out Vector2 point);
            Vector2 value = Vector2.ClampMagnitude(point / 70, 1);
            lastValue = value;
            handle.anchoredPosition = value * 52;
            if (attack)
            {
                ApplyAim(aimedThisGesture || (data.position - pressPosition).sqrMagnitude > 144 || Time.unscaledTime - pressedAt >= .18f);
            }
            else NetworkBrawler.TouchMove = value;
        }
        public void OnPointerUp(PointerEventData data)
        {
            if (data.pointerId != pointer) return;
            OnDrag(data);
            ResetStick(true);
        }
        private void Update()
        {
            if (visual != null && ((RectTransform)transform).rect.size != areaSize)
            {
                UpdateHome();
                ResetStick(false);
            }
            if (pointer != int.MinValue && (!NetworkBrawler.InputEnabled || NetworkBrawler.Local == null || !NetworkBrawler.Local.Alive.Value))
                ResetStick(false);
            else if (attack && pointer != int.MinValue) ApplyAim(Time.unscaledTime - pressedAt >= .18f);
        }
        private void ApplyAim(bool deliberate)
        {
            // A quick tap leaves the previous aim intact; crossing the centre also retains it.
            if (!deliberate || lastValue.sqrMagnitude <= .01f) return;
            NetworkBrawler.TouchAim = lastValue;
            NetworkBrawler.TouchAiming = true;
            aimedThisGesture = true;
        }
        private void PlaceCompanion()
        {
            if (companion == null || visual == null) return;
            var parent = (RectTransform)companion.parent;
            Vector2 center = parent.InverseTransformPoint(visual.position);
            companion.anchoredPosition = new Vector2(Mathf.Clamp(center.x - 158, parent.rect.xMin + 66, parent.rect.xMax - 66),
                Mathf.Clamp(center.y + 64, parent.rect.yMin + 66, parent.rect.yMax - 66)) - parent.rect.center;
        }
        private void OnDisable() => ResetStick(false);
        private void OnApplicationPause(bool paused) { if (paused) ResetStick(false); }
        private void OnApplicationFocus(bool focused) { if (!focused) ResetStick(false); }
        private void ResetStick(bool released)
        {
            pointer = int.MinValue;
            if (handle != null) handle.anchoredPosition = Vector2.zero;
            if (visual != null) visual.anchoredPosition = home;
            if (visualGroup != null) visualGroup.alpha = .55f;
            PlaceCompanion();
            if (attack)
            {
                NetworkBrawler.TouchAiming = false;
                if (released && NetworkBrawler.InputEnabled && NetworkBrawler.Local != null && NetworkBrawler.Local.Alive.Value)
                {
                    NetworkBrawler.TouchAttack = true;
                    NetworkBrawler.TouchQuickFire = !aimedThisGesture;
                }
                else if (!released) NetworkBrawler.TouchAttack = NetworkBrawler.TouchQuickFire = false;
            }
            else NetworkBrawler.TouchMove = Vector2.zero;
            aimedThisGesture = false;
        }
    }
}
