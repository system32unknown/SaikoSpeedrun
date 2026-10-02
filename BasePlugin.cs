using BepInEx;
using HarmonyLib;
using UnityEngine;
using SaikoMod;

namespace SaikoRenderer {
    [BepInPlugin(modGUID, "SaikoSpeedrun", "1.0.0.0")]
    [BepInDependency("Altertoriel.SaikoMod", Main.modVer)]
    [BepInProcess("Saiko no sutoka.exe")]
    public class BasePlugin : BaseUnityPlugin {
        const string modGUID = "altertoriel.saiko.saikospeedrun";
        public static BasePlugin plugin { get; private set; }

        void Awake() {
            Harmony harmony = new Harmony(modGUID);
            plugin = this;
            DontDestroyOnLoad(new GameObject("SpeedrunTimer", typeof(SpeedrunTimer)));

            harmony.PatchAllConditionals();
        }
    }
}