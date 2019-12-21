using System.Collections.Generic;
using System;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Bradley Guards", "Bazz3l", "1.0.3")]
    [Description("Spawns chinook event on bradley when taken down")]
    class BradleyGuards : RustPlugin
    {
        [PluginReference] Plugin Kits;

        private const string lockedPrefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
        private const string ch47Prefab   = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
        private HashSet<NPCPlayerApex> Guards = new HashSet<NPCPlayerApex>();
        private CH47LandingZone landingZone;
        private Vector3 chinkookPos;
        private Vector3 landingPos;
        private Quaternion landingRot;
        private Vector3 eventPos;
        private Quaternion eventRot;
        private static BradleyGuards ins;
        private bool hasLaunch = false;   

        #region Config
        public PluginConfig config;

        public PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                GuardAggressionRange = 201f,
                GuardDeaggroRange    = 202f,
                GuardVisionRange     = 203f,
                GuardDamageScale     = 0.2f,
                GuardMaxSpawn        = 11,
                GuardMaxRoam         = 10,
                GuardKit             = "guard"
            };
        }

        public class PluginConfig
        {
            public float GuardAggressionRange;   
            public float GuardDeaggroRange;
            public float GuardVisionRange;
            public float GuardDamageScale;
            public int GuardMaxSpawn;
            public int GuardMaxRoam;
            public string GuardKit;
        }
        #endregion

        #region Oxide
        protected override void LoadDefaultConfig() => Config.WriteObject(GetDefaultConfig(), true);
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> {
               ["EventStart"] = "Bradley Event: Guards arriving, stay clear or fight for the loot.",
            }, this);
        }

        void OnServerInitialized()
        {
            SetupMonument();
        }

        void Init()
        {
            config = Config.ReadObject<PluginConfig>();
            ins    = this;
        }

        void Unload()
        {
            ClearLandingZone();
            ClearGuards();
        }

        void OnEntitySpawned(BradleyAPC bradley)
        {
            if (bradley == null) return;
            
            ClearLandingZone();
            ClearGuards();
        }

        void OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            if (player == null || info?.Initiator == null) return;

            NPCPlayerApex npc = info.Initiator as NPCPlayerApex;
            if (!Guards.Contains(npc)) return;
            info.damageTypes.ScaleAll(config.GuardDamageScale);
        }

        void OnEntityKill(BradleyAPC bradley)
        {
            if (bradley == null || !hasLaunch) return;

            SpawnEvent(bradley.transform.position, bradley.transform.rotation);
        }

        void OnEntityDismounted(BaseMountable mountable, NPCPlayerApex npc)
        {
            if (npc == null || !Guards.Contains(npc)) return;

            npc.NavAgent.enabled = true;
            npc.SetFact(NPCPlayerApex.Facts.IsMounted, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.WantsToDismount, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.CanNotWieldWeapon, (byte) 0, true, true);
            npc.Resume();
        }
        #endregion

        #region Core
        void SpawnEvent(Vector3 pos, Quaternion rot)
        {
            eventPos    = pos;
            eventRot    = rot;
            landingZone = CreateLandingZone();

            CH47HelicopterAIController chinook = GameManager.server.CreateEntity(ch47Prefab, chinkookPos, landingRot) as CH47HelicopterAIController;
            if (chinook == null) return;
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

                Guards.Add(npc);

                npc.gameObject.AddComponent<BradleyGuard>().desPos = eventPos + (UnityEngine.Random.onUnitSphere * 10);
            }

            MessagePlayers("EventStart");

            timer.Once(120f, () => ClearLandingZone());
        }

        CH47LandingZone CreateLandingZone()
        {
            return new GameObject("helipad") { 
                layer     = 16, 
                transform = { 
                    position = landingPos, 
                    rotation = landingRot 
                }
            }.AddComponent<CH47LandingZone>();
        }

        void ClearLandingZone()
        {
            if (landingZone != null)
                UnityEngine.GameObject.Destroy(landingZone);
        }

        void ClearGuards()
        {
            foreach(BradleyGuard gameObj in UnityEngine.Object.FindObjectsOfType<BradleyGuard>())
                UnityEngine.Object.Destroy(gameObj);

            Guards.Clear();
        }

        void SetupMonument()
        {
            foreach (MonumentInfo monumentInfo in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                if (!monumentInfo.gameObject.name.Contains("launch_site_1")) continue;
                landingRot   = monumentInfo.transform.rotation;
                landingPos   = monumentInfo.transform.position + monumentInfo.transform.right * 125f;
                landingPos.y += 5f;

                chinkookPos = monumentInfo.transform.position + -monumentInfo.transform.right * 125f;
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
            public bool goingHome = false;
            public int roamRadius = 20;

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
                npc.Stats.AggressionRange = ins.config.GuardAggressionRange;
                npc.Stats.VisionRange     = ins.config.GuardVisionRange;
                npc.Stats.DeaggroRange    = ins.config.GuardDeaggroRange;
                npc.Stats.LongRange       = 80f;
                npc.Stats.Hostility       = 1f;
                npc.Stats.Defensiveness   = 1f;
                npc.Stats.OnlyAggroMarkedTargets = false;
                npc.InitFacts();
                npc.inventory.Strip();

                Interface.Oxide.CallHook("GiveKit", npc, ins.config.GuardKit);
            }

            void FixedUpdate()
            {
                if (!npc.IsNavRunning()) return;

                ShouldRelocate();
            }

            void OnDestroy()
            {
                npc?.KillMessage();
            }

            void ShouldRelocate()
            {
                float distance = Vector3.Distance(transform.position, desPos);
                if (!goingHome && distance >= ins.config.GuardMaxRoam)
                {
                    goingHome = true;
                }

                if (goingHome && distance >= ins.config.GuardMaxRoam)
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

        void MessagePlayers(string key)
        {
            foreach(BasePlayer player in BasePlayer.activePlayerList)
                Popup(player, Lang(key, player.UserIDString));
        }

        void Popup(BasePlayer player, string message, params object[] args)
        {
            if (player == null) return;
            player?.SendConsoleCommand("gametip.hidegametip");
            player?.SendConsoleCommand("gametip.showgametip", string.Format("<size=8>" + message + "</size>", args));
            ins.timer.Once(5f, () => player?.SendConsoleCommand("gametip.hidegametip"));
        }
        #endregion
    }
}
