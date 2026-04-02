using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace CookBook
{
    internal sealed class ColorFaderRuntime : MonoBehaviour
    {
        private Graphic _targetGraphic;
        private Coroutine _activeRoutine;

        private void Awake()
        {
            _targetGraphic = GetComponent<Graphic>();
        }

        public void CrossFadeColor(Color targetColor, float duration, bool ignoreTimeScale = true)
        {
            if (_targetGraphic == null) return;
            if (_activeRoutine != null) StopCoroutine(_activeRoutine);
            if (_targetGraphic.color == targetColor) return;
            if (duration <= 0f || !gameObject.activeInHierarchy)
            {
                _targetGraphic.color = targetColor;
                return;
            }

            _activeRoutine = StartCoroutine(FadeRoutine(targetColor, duration, ignoreTimeScale));
        }

        private IEnumerator FadeRoutine(Color target, float duration, bool ignoreTimeScale)
        {
            Color start = _targetGraphic.color;
            float timer = 0f;

            while (timer < duration)
            {
                timer += ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                float t = Mathf.Clamp01(timer / duration);

                _targetGraphic.color = Color.Lerp(start, target, t);
                yield return null;
            }

            _targetGraphic.color = target;
            _activeRoutine = null;
        }

        private void OnDisable()
        {
            if (_activeRoutine != null) StopCoroutine(_activeRoutine);
            _activeRoutine = null;
        }
    }
}
