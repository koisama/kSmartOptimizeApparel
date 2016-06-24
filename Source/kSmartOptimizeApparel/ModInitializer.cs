using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;

namespace kSmartOptimizeApparel
{
    public class ModInitializer : ITab
    {
        protected GameObject modInitializerControllerObject;

        public ModInitializer()
        {
            modInitializerControllerObject = new GameObject("ModInitializer");
            modInitializerControllerObject.AddComponent<ModInitializerBehaviour>();
            UnityEngine.Object.DontDestroyOnLoad((UnityEngine.Object)modInitializerControllerObject);
        }

        protected override void FillTab() { }
    }

    class ModInitializerBehaviour : MonoBehaviour
    {
        protected bool reinjectNeeded = false;
        protected float reinjectTime = 0;

        public void OnLevelWasLoaded(int level)
        {
            reinjectNeeded = true;
            if (level >= 0)
                reinjectTime = 1;
            else
                reinjectTime = 0;
        }

        public void FixedUpdate()
        {
            if (reinjectNeeded)
            {
                reinjectTime -= Time.fixedDeltaTime;

                if (reinjectTime <= 0)
                {
                    reinjectNeeded = false;
                    reinjectTime = 0;
                }
            }
        }

        public void Start()
        {
            MethodInfo coreMethod = typeof(JobGiver_OptimizeApparel).GetMethod("TryGiveTerminalJob", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo moddedMethod = typeof(kJobGiver_OptimizeApparel).GetMethod("TryGiveTerminalJob", BindingFlags.Instance | BindingFlags.NonPublic);

            if (!CommunityCoreLibrary.Detours.TryDetourFromTo(coreMethod, moddedMethod))
            {
                Log.Error("kSmartOptimizeApparel: Error detouring mod.");
            }

            OnLevelWasLoaded(-1);
        }
    }
}
