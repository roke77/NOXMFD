using System.Collections;
using System.Reflection;

namespace NOXMFD
{
    // Shared reflection into CountermeasureManager's private station list and each station's own
    // GetFirstCountermeasure() — both TelemetryReader.cs (reads the active station's category) and
    // Keybinds.cs (searches for the station matching a requested category) need the same two private
    // members, previously each hand-rolling their own FieldInfo/MethodInfo cache for the identical
    // field/method names (docs/refactor-scan.md step 8). The read-one-vs-search-many logic on top
    // stays in each caller — only the reflection access itself lives here.
    internal static class CmReflection
    {
        private static FieldInfo?  _stationsField;
        private static MethodInfo? _getFirstMethod;

        // CountermeasureManager.countermeasureStations, or null if the field couldn't be resolved.
        public static IList? GetStations(CountermeasureManager mgr)
        {
            if (_stationsField == null)
                _stationsField = typeof(CountermeasureManager)
                    .GetField("countermeasureStations", BindingFlags.NonPublic | BindingFlags.Instance);
            return _stationsField?.GetValue(mgr) as IList;
        }

        // A station's own GetFirstCountermeasure(), or null if it has none loaded (or the method
        // couldn't be resolved). Cached against the first station's runtime type — every station in
        // the list shares the same type, same assumption the two original copies both made.
        public static Countermeasure? GetFirstCountermeasure(object station)
        {
            if (_getFirstMethod == null)
                _getFirstMethod = station.GetType()
                    .GetMethod("GetFirstCountermeasure", BindingFlags.Public | BindingFlags.Instance);
            return _getFirstMethod?.Invoke(station, null) as Countermeasure;
        }
    }
}
