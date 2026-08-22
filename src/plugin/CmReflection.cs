using System.Collections;
using System.Reflection;

namespace NOXMFD
{
    // Shared reflection cache for CountermeasureManager's private station list and each station's
    // GetFirstCountermeasure(), used by TelemetryReader and Keybinds.
    internal static class CmReflection
    {
        private static FieldInfo?  _stationsField;
        private static MethodInfo? _getFirstMethod;

        // CountermeasureManager.countermeasureStations, or null if unresolved.
        public static IList? GetStations(CountermeasureManager mgr)
        {
            if (_stationsField == null)
                _stationsField = typeof(CountermeasureManager)
                    .GetField("countermeasureStations", BindingFlags.NonPublic | BindingFlags.Instance);
            return _stationsField?.GetValue(mgr) as IList;
        }

        // Cached against the first station's type; assumes all stations share one runtime type.
        public static Countermeasure? GetFirstCountermeasure(object station)
        {
            if (_getFirstMethod == null)
                _getFirstMethod = station.GetType()
                    .GetMethod("GetFirstCountermeasure", BindingFlags.Public | BindingFlags.Instance);
            return _getFirstMethod?.Invoke(station, null) as Countermeasure;
        }
    }
}
