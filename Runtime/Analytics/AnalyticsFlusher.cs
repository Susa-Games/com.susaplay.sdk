using UnityEngine;
using System.Collections;
namespace susaplay.SDK
{
    public class AnalyticsFlusher : MonoBehaviour
    {
        private AnalyticsModule _module;
        private Coroutine _flushLoop;
        private bool _flushOnPause;
        private bool _flushOnQuit;
        private bool _isFlushing;

        public void Initialize(
            AnalyticsModule module,
            float intervalSeconds,
            bool flushOnInitialize,
            bool flushOnPause,
            bool flushOnQuit)
        {
            _module = module;
            _flushOnPause = flushOnPause;
            _flushOnQuit = flushOnQuit;

            if (flushOnInitialize)
            {
                FlushNow();
            }

            if (intervalSeconds > 0f)
            {
                _flushLoop = StartCoroutine(FlushLoop(intervalSeconds));
            }
        }

        private IEnumerator FlushLoop(float intervalSeconds)
        {
            while (true)
            {
                yield return new WaitForSeconds(intervalSeconds);
                FlushNow();
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused && _flushOnPause)
            {
                FlushNow();
            }
        }

        private void OnApplicationQuit()
        {
            if (_flushOnQuit)
            {
                FlushNow();
            }
        }

        private async void FlushNow()
        {
            if (_module == null || _isFlushing)
            {
                return;
            }

            _isFlushing = true;
            try
            {
                await _module.Flush();
            }
            finally
            {
                _isFlushing = false;
            }
        }

        private void OnDestroy()
        {
            if (_flushLoop != null)
            {
                StopCoroutine(_flushLoop);
                _flushLoop = null;
            }
        }
    }
}
