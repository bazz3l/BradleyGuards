using System.Collections.Generic;
using System;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Bradley Guards", "Bazz3l", "1.1.8")]
    [Description("Calls in reinforcements when bradley is taken down")]
    class BradleyGuards : RustPlugin
    {
        [PluginReference] Plugin Kits;

        #region Fields
        const string lockedPrefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
        const string ch47Prefab   = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
        const string landingName  = "BradleyLandingZone";
        HashSet<CH47LandingZone> zones = new HashSet<CH47LandingZone>();
        HashSet<NPCPlayerApex> npcs = new HashSet<NPCPlayerApex>();
        Quaternion landingRotation;
        Vector3 landingPosition;        
        Vector3 chinookPosition;
        bool hasLaunch;
        static BradleyGuards plugin;
        #endregion

        #region Config
        PluginConfig config;

        protected override void LoadDefaultConfig() => Config.WriteObject(GetDefaultConfig(), true);

        PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                GuardMaxSpawn        = 11, // Max is 11
                GuardMaxRoam         = 30,
                GuardAggressionRange = 101f,
                GuardVisionRange     = 103f,
                GuardLongRange       = 100f,
                GuardDeaggroRange    = 104f,
                GuardDamageScale     = 0.5f,
                GuardName            = "Guard",
                GuardKit             = "guard"
            };
        }

        class PluginConfig
        {
            public float GuardAggressionRange;
            public float GuardDeaggroRange;
            public float GuardVisionRange;
            public float GuardLongRange;
            public float GuardDamageScale;
            public int GuardMaxSpawn;
            public int GuardMaxRoam;
            public string GuardName;
            public string GuardKit;
        }
        #endregion

        #region Oxide
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> {
                {"EventStart", "<color=#DC143C>Bradley Reinforcements</color>: stay clear or fight for the loot."}
            }, this);
        }

        void OnServerInitialized()
        {
            plugin = this;

            CheckLandingPoint();
            CleanUp();

            if (!hasLaunch)
            {
                return;
            }

            CH47LandingZone zone = CreateLandingZone();

            zones.Add(zone);
        }

        void Init()
        {
            config = Config.ReadObject<PluginConfig>();
        }

        void Unload() => CleanUp();

        void OnEntitySpawned(BradleyAPC bradley) => ClearGuards();

        void OnEntityDeath(BradleyAPC bradley)
        {
            if (bradley == null || !hasLaunch)
            {
                return;
            }

            SpawnEvent(bradley.transform.position, bradley.transform.rotation);
        }

        void OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            if (info == null || info?.Initiator == null || !(info?.Initiator is NPCPlayerApex))
            {
                return;
            }

            NPCPlayerApex npc = info.Initiator as NPCPlayerApex;
            if (npc.GetComponent<BradleyGuard>() == null)
            {
                return;
            }

            info.damageTypes.ScaleAll(config.GuardDamageScale);
        }

        void OnEntityDismounted(BaseMountable mountable, NPCPlayerApex npc)
        {
            if (npc.GetComponent<BradleyGuard>() == null)
            {
                return;
            }

            npc.SetFact(NPCPlayerApex.Facts.CanNotWieldWeapon, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.WantsToDismount,   (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.IsMounted,         (byte) 0, true, true);
            npc.Resume();
        }
        #endregion

        #region Core
        void SpawnEvent(Vector3 eventPos, Quaternion eventRot)
        {
            CH47HelicopterAIController chinook = GameManager.server.CreateEntity(ch47Prefab, chinookPosition, landingRotation) as CH47HelicopterAIController;
            if (chinook == null)
            {
                return;
            }

            chinook.SetLandingTarget(landingPosition);
            chinook.SetMoveTarget(landingPosition);
            chinook.hoverHeight = 1.5f;
            chinook.Spawn();
            chinook.CancelInvoke(new Action(chinook.SpawnScientists));

            for (int i = 0; i < config.GuardMaxSpawn; i++)
            {
                chinook.SpawnScientist(chinook.transform.position + (chinook.transform.forward * 10f));
            }

            for (int j = 0; j < 1; j++)
            {
                chinook.SpawnScientist(chinook.transform.position - (chinook.transform.forward * 5f));
            }

            foreach(BaseVehicle.MountPointInfo mountPoint in chinook.mountPoints)
            {
                NPCPlayerApex npc = mountPoint.mountable.GetMounted().GetComponent<NPCPlayerApex>();
                if (npc == null || npc.IsDestroyed)
                {
                    continue;
                }

                BradleyGuard guard  = npc.gameObject.AddComponent<BradleyGuard>();
                guard.spawnPosition = RandomCircle(eventPos, 10f);
                guard.eventCenter   = eventPos;

                npcs.Add(npc);
            }

            MessageAll();
        }

        CH47LandingZone CreateLandingZone()
        {
            return new GameObject(landingName) {
                layer     = 16, 
                transform = { 
                    position = landingPosition, 
                    rotation = landingRotation 
                }
            }.AddComponent<CH47LandingZone>();
        }

        void CleanUp()
        {
            ClearGuards();
            ClearZones();
        }

        void ClearZones()
        {
            foreach(CH47LandingZone zone in zones) UnityEngine.GameObject.Destroy(zone.gameObject);

            zones.Clear();
        }

        void ClearGuards()
        {
            foreach(NPCPlayerApex npc in npcs)
            {
                if (npc != null && !npc.IsDestroyed)
                {
                    npc?.Kill();
                }
            }

            npcs.Clear();
        }

        void CheckLandingPoint()
        {
            foreach (MonumentInfo monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                if (!monument.gameObject.name.Contains("launch_site_1"))
                {
                    continue;
                }

                landingRotation = monument.transform.rotation;
                landingPosition = monument.transform.position + monument.transform.right * 125f;
                landingPosition.y += 5f;

                chinookPosition = monument.transform.position + -monument.transform.right * 125f;
                chinookPosition.y += 150f;

                hasLaunch = true;
            };
        }
        #endregion

        #region Classes
        class BradleyGuard : MonoBehaviour
        {
            NPCPlayerApex npc;
            public Vector3 spawnPosition;
            public Vector3 eventCenter;
            bool moveBack;

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
                npc.displayName           = plugin.config.GuardName;
                npc.Stats.AggressionRange = plugin.config.GuardAggressionRange;
                npc.Stats.VisionRange     = plugin.config.GuardVisionRange;
                npc.Stats.DeaggroRange    = plugin.config.GuardDeaggroRange;
                npc.Stats.LongRange       = plugin.config.GuardLongRange;
                npc.Stats.Hostility       = 1;
                npc.Stats.Defensiveness   = 1;

                npc.inventory.Strip();

                Interface.Oxide.CallHook("GiveKit", npc, plugin.config.GuardKit);
            }

            void FixedUpdate() => ShouldRelocate();

            void OnDestroy()
            {
                if (npc == null || npc.IsDestroyed)
                {
                    return;
                }

                npc?.Kill();
            }

            void ShouldRelocate()
            {
                if (npc == null || npc.IsDestroyed)
                {
                    return;
                }

                float distance  = Vector3.Distance(transform.position, eventCenter);
                bool shouldMove = (!IsAggro() && distance >= 10 || IsAggro() && distance >= plugin.config.GuardMaxRoam);

                if(!moveBack && shouldMove)
                {
                    moveBack = true;
                }

                if (moveBack && shouldMove)
                {
                    if (npc.IsNavRunning())
                        npc.GetNavAgent.SetDestination(spawnPosition);
                    else
                        npc.finalDestination = spawnPosition;
                }
                else
                {
                    moveBack = false;
                }
            }

            bool IsAggro()
            {
                return npc.GetFact(NPCPlayerApex.Facts.IsAggro) != (byte) 0;
            }
        }
        #endregion

        #region Helpers
        string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        Vector3 RandomCircle(Vector3 center, float radius)
        {
            float angle = UnityEngine.Random.Range(0f, 100f) * 360;
            Vector3 pos = center;
            pos.x += radius * Mathf.Sin(angle * Mathf.Deg2Rad);
            pos.z += radius * Mathf.Cos(angle * Mathf.Deg2Rad);
            return pos;
        }

        void MessageAll()
        {
            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected)
                {
                    continue;
                }

                player.ChatMessage(Lang("EventStart", player.UserIDString));
            }
        }
        #endregion
    }
}
