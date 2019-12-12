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
        private const string LockedPrefab    = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
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
               ["EventStart"] = "Bradley Event:\nGuards arriving, stay clear or fight for the loot.",
            }, this);
        }

        void OnServerInitialized()
        {
            SetupMonumentPos();
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

        void OnEntityKill(BradleyAPC bradley)
        {
            if (bradley == null) return;
            EventPos = bradley.transform.position;
            StartEvent();
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
        void StartEvent()
        {
            if (!HasLaunch) return;

            LandingZone = CreateLandingZone();

            CH47HelicopterAIController chinook = GameManager.server.CreateEntity(CH47Prefab, LandingZonePos + new Vector3(100f,200f,500f)) as CH47HelicopterAIController;
            if (chinook == null) return;
            chinook.SetLandingTarget(LandingZonePos);
            chinook.hoverHeight = 5f;
            chinook.Spawn();
            chinook.CancelInvoke(new Action(chinook.SpawnScientists));

            for (int i = 0; i < config.GuardMaxSpawn; i++)
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
                if (mountPoint.mountable == null) continue;
                NPCPlayerApex npc = mountPoint.mountable.GetMounted().GetComponent<NPCPlayerApex>();
                if (npc == null) continue;
                Guards.Add(npc);
                npc.gameObject.AddComponent<BradleyGuard>().desPos = EventPos + (UnityEngine.Random.onUnitSphere * 10);
            }

            HackableCrate();

            MessagePlayers("EventStart");
        }

        void HackableCrate()
        {
            HackableLockedCrate crate = GameManager.server.CreateEntity(LockedPrefab, EventPos + new Vector3(0f,5f,0f)) as HackableLockedCrate;
            if (crate == null) return;
            crate.Spawn();
            crate.StartHacking();

            timer.Once(120f, () => ClearLandingZone());
        }

        CH47LandingZone CreateLandingZone()
        {
            return new GameObject("helipad") { 
                layer     = 16, 
                transform = { position = LandingZonePos }
            }.AddComponent<CH47LandingZone>();
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
            foreach (MonumentInfo monumentInfo in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
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
                npc.SpawnPosition         = desPos;
                npc.Destination           = desPos;
                npc.Stats.AggressionRange = ins.config.GuardAggressionRange;
                npc.Stats.VisionRange     = ins.config.GuardVisionRange;
                npc.Stats.DeaggroRange    = ins.config.GuardDeaggroRange;
                npc.Stats.LongRange       = 200f;
                npc.Stats.Hostility       = 1f;
                npc.Stats.Defensiveness   = 1f;
                npc.Stats.OnlyAggroMarkedTargets = false;
                npc.InitFacts();                
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
                var distance = Vector3.Distance(transform.position, desPos);
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
            {
                Popup(player, Lang(key, player.UserIDString));
            }
        }

        void Popup(BasePlayer player, string message, params object[] args)
        {
            if (player == null) return;
            player.SendConsoleCommand("gametip.hidegametip");
            player.SendConsoleCommand("gametip.showgametip", string.Format("<size=8>" + message + "</size>", args));
            ins.timer.Once(5f, () => player?.SendConsoleCommand("gametip.hidegametip"));
        }
        #endregion
    }
}

