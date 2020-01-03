using System.Collections.Generic;
using System;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Bradley Guards", "Bazz3l", "1.0.6")]
    [Description("Spawns an event when bradley is taken down")]
    class BradleyGuards : RustPlugin
    {
        [PluginReference] Plugin Kits;

        private const string lockedPrefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
        private const string ch47Prefab   = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
        private const string landingName  = "bradley-lzone";
        private HashSet<NPCPlayerApex> Guards = new HashSet<NPCPlayerApex>();
        private CH47LandingZone landingZone;
        private Vector3 chinkookPos;
        private Vector3 landingPos;
        private Quaternion landingRot;
        private static BradleyGuards plugin;
        private bool hasLaunch = false;

        #region Config
        public PluginConfig config;

        protected override void LoadDefaultConfig() => Config.WriteObject(GetDefaultConfig(), true);

        public PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                GuardAggressionRange = 101f,
                GuardVisionRange     = 103f,
                GuardLongRange       = 100f,
                GuardDeaggroRange    = 104f,
                GuardDamageScale     = 0.5f,
                GuardMaxSpawn        = 11,
                GuardMaxRoam         = 30,
                GuardKit             = "guard"
            };
        }

        public class PluginConfig
        {
            public float GuardAggressionRange;
            public float GuardDeaggroRange;
            public float GuardVisionRange;
            public float GuardLongRange;
            public float GuardDamageScale;
            public int GuardMaxSpawn;
            public int GuardMaxRoam;
            public string GuardKit;
        }
        #endregion

        #region Oxide
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> {
               ["EventStart"] = "Bradley Guards, stay clear or fight for the loot.",
            }, this);
        }

        private void OnServerInitialized()
        {
            plugin = this;

            SetupLandingPoint();
            ClearLandingZone();
            ClearGuards();

            if (!hasLaunch) return;

            landingZone = CreateLandingZone();
        }

        private void Init()
        {
            config = Config.ReadObject<PluginConfig>();

            //
        }

        private void Unload()
        {
            ClearLandingZone();
            ClearGuards();
        }

        private void OnEntitySpawned(BradleyAPC bradley)
        {
            if (bradley == null) return;

            ClearGuards();
        }

        private void OnEntityKill(BradleyAPC bradley)
        {
            if (bradley == null || !hasLaunch) return;

            Quaternion eventRot = bradley.transform.rotation;
            Vector3 eventPos    = bradley.transform.position;

            SpawnEvent(eventPos, eventRot);

            MessageAll("EventStart");
        }

        private void OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            NPCPlayerApex npc = info?.Initiator as NPCPlayerApex;
            if (npc == null || !Guards.Contains(npc)) return;

            info.damageTypes.ScaleAll(config.GuardDamageScale);
        }

        private void OnEntityDismounted(BaseMountable mountable, NPCPlayerApex npc)
        {
            if (npc == null || !Guards.Contains(npc)) return;

            npc.SetFact(NPCPlayerApex.Facts.IsMounted,         (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.WantsToDismount,   (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.CanNotWieldWeapon, (byte) 0, true, true);
            npc.Resume();
        }
        #endregion

        #region Core
        private void SpawnEvent(Vector3 eventPos, Quaternion eventRot)
        {
            CH47HelicopterAIController chinook = GameManager.server.CreateEntity(ch47Prefab, chinkookPos, landingRot) as CH47HelicopterAIController;
            chinook.SetLandingTarget(landingPos);
            chinook.hoverHeight = 1.5f;
            chinook.Spawn();
            chinook.CancelInvoke(new Action(chinook.SpawnScientists));

            for (int i = 0; i < config.GuardMaxSpawn; i++)
                chinook.SpawnScientist(chinook.transform.position + (chinook.transform.forward * 10f));

            for (int j = 0; j < 1; j++)
                chinook.SpawnScientist(chinook.transform.position - (chinook.transform.forward * 5f));

            foreach(BaseVehicle.MountPointInfo mountPoint in chinook.mountPoints)
            {
                NPCPlayerApex npc = mountPoint.mountable.GetMounted().GetComponent<NPCPlayerApex>();
                if (npc == null) continue;

                npc.gameObject.AddComponent<BradleyGuard>().desPos = eventPos + (UnityEngine.Random.onUnitSphere * 10);

                Guards.Add(npc);
            }
        }

        private void SpawnHackableCrate(Vector3 eventPos, Quaternion eventRot)
        {
            HackableLockedCrate crate = GameManager.server.CreateEntity(lockedPrefab, eventPos + (Vector3.forward * 5), eventRot) as HackableLockedCrate;
            crate.Spawn();
            crate.StartHacking();
        }

        private CH47LandingZone CreateLandingZone()
        {
            return new GameObject(landingName) { 
                layer     = 16, 
                transform = { position = landingPos, rotation = landingRot }
            }.AddComponent<CH47LandingZone>();
        }

        private void ClearLandingZone()
        {
            foreach(CH47LandingZone lzone in UnityEngine.Object.FindObjectsOfType<CH47LandingZone>())
            {
                if (!lzone.gameObject.name.Contains(landingName)) continue;
                
                UnityEngine.GameObject.Destroy(lzone.gameObject);
            }

            landingZone = null;
        }

        private void ClearGuards()
        {
            foreach(BradleyGuard guard in UnityEngine.Object.FindObjectsOfType<BradleyGuard>())
            {
                UnityEngine.Object.Destroy(guard);
            }

            Guards.Clear();
        }

        private void SetupLandingPoint()
        {
            foreach (MonumentInfo monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                if (!monument.gameObject.name.Contains("launch_site_1")) continue;

                landingRot   = monument.transform.rotation;
                landingPos   = monument.transform.position + monument.transform.right * 125f;
                landingPos.y += 5f;

                chinkookPos = monument.transform.position + -monument.transform.right * 125f;
                chinkookPos.y += 150f;

                hasLaunch = true;
            };
        }
        #endregion

        #region Classes
        class BradleyGuard : MonoBehaviour
        {
            public NPCPlayerApex npc;
            public Vector3 desPos;
            public bool goingHome;

            void Start()
            {
                npc = gameObject.GetComponent<NPCPlayerApex>();
                if (npc == null)
                {
                    Destroy(this);

                    return;
                }

                npc.RadioEffect           = new GameObjectRef();
                npc.DeathEffect           = new GameObjectRef();
                npc.SpawnPosition         = desPos;
                npc.Destination           = desPos;
                npc.Stats.AggressionRange = plugin.config.GuardAggressionRange;
                npc.Stats.VisionRange     = plugin.config.GuardVisionRange;
                npc.Stats.DeaggroRange    = plugin.config.GuardDeaggroRange;
                npc.Stats.LongRange       = plugin.config.GuardLongRange;
                npc.Stats.Hostility       = 1f;
                npc.Stats.Defensiveness   = 1f;
                npc.Stats.OnlyAggroMarkedTargets = false;
                npc.InitFacts();

                npc.inventory.Strip();

                Interface.Oxide.CallHook("GiveKit", npc, plugin.config.GuardKit);
            }

            void FixedUpdate()
            {
                if (npc == null || !npc.IsNavRunning()) return;

                ShouldRelocate();
            }

            void OnDestroy()
            {
                if (npc == null || npc.IsDestroyed) return;

                npc?.KillMessage();
            }

            void ShouldRelocate()
            {
                float distance = Vector3.Distance(transform.position, desPos);
                if(!goingHome && distance >= plugin.config.GuardMaxRoam)
                {
                    goingHome = true;
                }

                if (goingHome && distance >= plugin.config.GuardMaxRoam)
                {
                    npc.GetNavAgent.SetDestination(desPos);
                    npc.Destination = desPos;
                }
                else
                {
                    goingHome = false;
                }
            }
        }
        #endregion

        #region Helpers
        string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        void MessageAll(string key)
        {
            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected) continue;

                player.ChatMessage(Lang(key, player.UserIDString));
            }
        }
        #endregion
    }
}
