using System.Collections.Generic;
using System;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Bradley Guards", "Bazz3l", "1.0.0")]
    [Description("")]
    class BradleyGuards : RustPlugin
    {
        [PluginReference] Plugin Kits;

        private const string ScientistPrefab = "assets/prefabs/npc/scientist/scientist.prefab";
        private const string CH47Prefab      = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
        private List<NPCPlayerApex> Guards   = new List<NPCPlayerApex>();
        private CH47LandingZone LandingZone;
        private Vector3 LandingZonePos;
        private Vector3 EventPos;
        private bool HasLaunch = false;
        private static BradleyGuards ins;

        #region Config
        public PluginConfig config;

        public PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                GuardAggressionRange = 100f,
                GuardVisionRange     = 100f,
                GuardDamageScale     = 0.2f,
                GuardKit             = "guard"
            };
        }

        public class PluginConfig
        {
            public float GuardAggressionRange;            
            public float GuardVisionRange;
            public float GuardDamageScale;
            public string GuardKit;
        }
        #endregion

        #region Oxide
        protected override void LoadDefaultConfig() => Config.WriteObject(GetDefaultConfig(), true);
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> {
               ["EventStart"] = "<color=#DC143C>Bradley</color>: Armed guards flying in, stay clear or fight for the loot.",
            }, this);
        }

        void Init()
        {
            config = Config.ReadObject<PluginConfig>();
            ins    = this;

            SetupMonumentPos();
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

        void OnEntityKill(BradleyAPC bradley)
        {
            if (bradley == null) return;
            
            StartEvent(bradley.transform.position);

            MessagePlayers("EventStart");
        }

        void OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            if (info == null || info?.Initiator == null) return;
            NPCPlayerApex npc = info.Initiator as NPCPlayerApex;
            if (npc == null || !Guards.Contains(npc)) return;
            info.damageTypes.ScaleAll(config.GuardDamageScale);
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
        void StartEvent(Vector3 eventPos)
        {
            if (!HasLaunch) return;

            EventPos    = eventPos;
            LandingZone = CreateLandingZone();

            CH47HelicopterAIController chinook = GameManager.server.CreateEntity(CH47Prefab, LandingZonePos + new Vector3(100f,200f,500f)) as CH47HelicopterAIController;
            if (chinook == null) return;
            chinook.SetLandingTarget(LandingZonePos);
            chinook.hoverHeight = 0.1f;
            chinook.Spawn();
            chinook.CancelInvoke(new Action(chinook.SpawnScientists));

            for (int i = 0; i < 11; i++)
            {
                Vector3 passengerPos = chinook.transform.position + (chinook.transform.forward * 10f);
                chinook.SpawnScientist(passengerPos);
            }

            for (int j = 0; j < 1; j++)
            {
                Vector3 pilotPos = chinook.transform.position - (chinook.transform.forward * 5f);
                chinook.SpawnScientist(pilotPos);
            }

            foreach(BaseVehicle.MountPointInfo mountPoint in chinook.mountPoints)
            {
                if (mountPoint.mountable == null || !mountPoint.mountable.IsMounted()) continue;

                BasePlayer player = mountPoint.mountable.GetMounted();
                if (player == null) continue;

                NPCPlayerApex npc = player.GetComponent<NPCPlayerApex>();
                if (npc == null || Guards.Contains(npc)) continue;

                npc.gameObject.AddComponent<BradleyGuard>().desPos = EventPos + (UnityEngine.Random.onUnitSphere * 10);

                Guards.Add(npc);
            }

            timer.Once(120f, () => ClearLandingZone());
        }

        CH47LandingZone CreateLandingZone()
        {
            return new GameObject("helipad") { layer = 16, transform = { position = LandingZonePos }}.AddComponent<CH47LandingZone>();
        }

        void ClearLandingZone()
        {
            if (LandingZone != null)
                UnityEngine.GameObject.Destroy(LandingZone);
        }

        void ClearGuards()
        {
            foreach(BradleyGuard gameObj in UnityEngine.Object.FindObjectsOfType<BradleyGuard>())
            {
                UnityEngine.Object.Destroy(gameObj);
            }

            Guards.Clear();
        }

        void SetupMonumentPos()
        {
            MonumentInfo[] monumentInfos = UnityEngine.Object.FindObjectsOfType<MonumentInfo>();
            foreach (MonumentInfo monumentInfo in monumentInfos)
            {
                if (!monumentInfo.gameObject.name.Contains("launch_site_1")) continue;
                Vector3 pos    = monumentInfo.transform.position + monumentInfo.transform.right * 125f;
                pos.y          = pos.y + 5f;
                LandingZonePos = pos;
                HasLaunch      = true;
            };
        }
        #endregion

        #region Classes
        class BradleyGuard : MonoBehaviour
        {
            public NPCPlayerApex npc;
            public Vector3 desPos;
            public bool goingHome = false;
            public int roamRadius = 10;

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
                npc.Stats.AggressionRange = ins.config.GuardAggressionRange;
                npc.Stats.VisionRange     = ins.config.GuardVisionRange;
                npc.Stats.Hostility       = 1f;
                npc.SpawnPosition         = desPos;
                npc.Destination           = desPos;
                npc.inventory.Strip();

                Interface.Oxide.CallHook("GiveKit", npc, ins.config.GuardKit);
            }

            void FixedUpdate()
            {
                if (!npc.IsNavRunning() || (npc.GetFact(NPCPlayerApex.Facts.IsAggro)) == 1) return;

                ShouldRelocate();
            }

            void OnDestroy()
            {
                npc?.KillMessage();
            }

            void ShouldRelocate()
            {
                float distance = Vector3.Distance(npc.transform.position, desPos);

                if (!goingHome && distance >= roamRadius)
                {
                    goingHome = true;
                }

                if (goingHome && distance >= roamRadius)
                {
                    npc.SetDestination(desPos);
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
            {
                player.ChatMessage(Lang(key, player.UserIDString));
            }
        }
        #endregion
    }
}
