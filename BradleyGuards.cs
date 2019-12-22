using System.Collections.Generic;
using System;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Bradley Guards", "Bazz3l", "1.0.4")]
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

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Loading Default Config");
        }

        void LoadConfig()
        {
            Config.Clear();
            config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(config, true);
        }

        public PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                UsePopupMessage      = true,
                GuardAggressionRange = 201f,
                GuardDeaggroRange    = 202f,
                GuardVisionRange     = 203f,
                GuardLongRange       = 100f,
                GuardDamageScale     = 0.5f,
                GuardMaxSpawn        = 11,
                GuardMaxRoam         = 80,
                GuardKit             = "guard"
            };
        }

        public class PluginConfig
        {
            public bool UsePopupMessage;
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

        void OnServerInitialized()
        {
            ins = this;

            SetupMonument();
        }

        void Init()
        {
            LoadConfig();
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

        void OnEntityDismounted(BaseMountable mountable, NPCPlayerApex npc)
        {
            if (npc == null || !Guards.Contains(npc)) return;

            npc.NavAgent.enabled = true;
            npc.SetFact(NPCPlayerApex.Facts.IsMounted, (byte) 0, false, false);
            npc.SetFact(NPCPlayerApex.Facts.WantsToDismount, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.CanNotWieldWeapon, (byte) 0, false, false);
            npc.Resume();
        }

        void OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            NPCPlayerApex npc = info?.Initiator as NPCPlayerApex;

            if (npc == null || !Guards.Contains(npc)) return;

            info.damageTypes.ScaleAll(config.GuardDamageScale);
        }

        void OnEntityKill(BradleyAPC bradley)
        {
            if (bradley == null || !hasLaunch) return;

            SpawnEvent(bradley.transform.position, bradley.transform.rotation);
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

            SpawnHackableCrate(pos, rot);

            MessageAll("EventStart");

            timer.Once(120f, () => ClearLandingZone());
        }

        void SpawnHackableCrate(Vector3 pos, Quaternion rot)
        {
            HackableLockedCrate crate = GameManager.server.CreateEntity(lockedPrefab, pos + (Vector3.forward * 5), rot) as HackableLockedCrate;
            if (crate == null) return;
            crate.Spawn();
            crate.StartHacking();
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
                npc.Stats.AggressionRange = ins.config.GuardAggressionRange;
                npc.Stats.VisionRange     = ins.config.GuardVisionRange;
                npc.Stats.DeaggroRange    = ins.config.GuardDeaggroRange;
                npc.Stats.LongRange       = ins.config.GuardLongRange;
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
                if(!goingHome && distance >= ins.config.GuardMaxRoam)
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

        void MessageAll(string key)
        {
            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                if (config.UsePopupMessage)
                    Popup(player, Lang(key, player.UserIDString));
                else
                    player.ChatMessage(Lang(key, player.UserIDString));
            }
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
