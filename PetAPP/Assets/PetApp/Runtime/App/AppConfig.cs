using UnityEngine;

namespace PetApp.App
{
    public sealed class AppConfig : ScriptableObject
    {
        public string catalogUrl = "https://aliameen.com/petapp/catalog.json";

        public static AppConfig Load() => Resources.Load<AppConfig>("PetApp/AppConfig");
    }
}
