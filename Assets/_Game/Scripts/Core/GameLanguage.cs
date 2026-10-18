using System.Globalization;
using UnityEngine;

namespace FPSLAN
{
    public static class GameLanguage
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            var english = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentCulture = english;
            CultureInfo.DefaultThreadCurrentUICulture = english;
            CultureInfo.CurrentCulture = english;
            CultureInfo.CurrentUICulture = english;
        }
    }
}
