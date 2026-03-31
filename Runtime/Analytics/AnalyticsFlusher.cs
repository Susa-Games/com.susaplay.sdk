using UnityEngine;
using System.Collections;
using System.Threading.Tasks;
namespace susaplay.SDK
{
    public class AnalyticsFlusher : MonoBehaviour
    {
        private AnalyticsModule _module;
        public void Initialize(AnalyticsModule module)
        {
            _module = module;
            StartCoroutine(FlushLoop());
        }

        private IEnumerator FlushLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(30f);
                _ = _module.Flush();
            }
        }
    }
}