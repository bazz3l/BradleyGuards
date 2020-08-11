using System.Collections.Generic;
using System.Linq;
using System;
using Oxide.Core.Plugins;
using Oxide.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Bradley Guards", "Bazz3l", "1.2.3")]
    [Description("Calls for reinforcements when bradley is destroyed.")]
    class BradleyGuards : RustPlugin
    {
        [PluginReference] Plugin Kits;

        #region Fields
        const string ch47Prefab = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
        const string landingName = "BradleyLandingZone";

        HashSet<CH47LandingZone> zones = new HashSet<CH47LandingZone>();
        HashSet<NPCPlayerApex> npcs = new HashSet<NPCPlayerApex>();

        PluginConfig config;
        Quaternion landingRotation;
        Vector3 landingPosition;
        Vector3 chinookPosition;
        Vector3 bradleyPosition;
        bool hasLaunch;

        static BradleyGuards plugin;
        #endregion

        #region Config
        protected override void LoadDefaultConfig() => Config.WriteObject(GetDefaultConfig(), true);

        PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                GuardMaxSpawn = 11, // Max is 11
                CrateAmount   = 4,
                GuardSettings = new List<GuardSetting> {
                    new GuardSetting("Heavy Gunner", 100f)
                }
            };
        }

        class PluginConfig
        {
            [JsonProperty(PropertyName = "CrateAmount (max amount of crates bradley will spawn)")]
            public int CrateAmount;

            [JsonProperty(PropertyName = "GuardMaxSpawn (max number of guard to spawn note: 11 is max)")]
            public int GuardMaxSpawn;

            [JsonProperty(PropertyName = "GuardSettings (create different types of guards must contain atleast 1)")]
            public List<GuardSetting> GuardSettings;
        }

        class GuardSetting
        {
            [JsonProperty(PropertyName = "Name (npc display name)")]
            public string Name;

            [JsonProperty(PropertyName = "Health (sets the health of npc)")]
            public float Health = 100f;

            [JsonProperty(PropertyName = "MaxRoamRadius (max roam radius)")]
            public float MaxRoamRadius;

            [JsonProperty(PropertyName = "MaxRange (max distance they will shoot)")]
            public float MaxRange = 150f;

            [JsonProperty(PropertyName = "KitName (custom kit name)")]
            public string KitName = "";

            [JsonProperty(PropertyName = "KitEnabled (enable custom kit)")]
            public bool KitEnabled = false;

            public GuardSetting(string name, float health, float maxRoamRadius = 80f)
            {
                Name = name;
                Health = health;
                MaxRoamRadius = maxRoamRadius;
            }
        }
        #endregion

        #region Oxide
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> {
                {"EventStart", "[<color=#DC143C>Bradley Guards</color>]: Guards are on route, prepare to fight or run for your life."},
                {"EventEnded", "[<color=#DC143C>Bradley Guards</color>]: Guards down, all clear loot up fast."},
            }, this);
        }

        void OnServerInitialized()
        {
            CheckLandingPoint();

            if (!hasLaunch)
            {
                return;
            }

            CH47LandingZone zone = CreateLandingZone();

            zones.Add(zone);
        }

        void Init()
        {
            plugin = this;

            config = Config.ReadObject<PluginConfig>();
        }

        void Unload() => CleanUp();

        void OnEntitySpawned(BradleyAPC bradley)
        {
            bradley.maxCratesToSpawn = config.CrateAmount;

            ClearGuards();
        }

        void OnEntityDeath(BradleyAPC bradley, HitInfo info) => SpawnEvent(bradley.transform.position);

        void OnEntityDeath(NPCPlayerApex npc, HitInfo info) => RemoveNPC(npc);

        void OnEntityDismounted(BaseMountable mountable, NPCPlayerApex npc)
        {
            if (npc == null || !npcs.Contains(npc))
            {
                return;
            }

            npc.SetFact(NPCPlayerApex.Facts.IsMounted, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.WantsToDismount, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.CanNotWieldWeapon, (byte) 0, true, true);
            npc.Resume();
        }
        #endregion

        #region Core
        void SpawnEvent(Vector3 position)
        {
            if (!hasLaunch)
            {
                return;
            }

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
                SpawnScientist(chinook, config.GuardSettings.GetRandom(), chinook.transform.position + (chinook.transform.forward * 10f), position);
            }

            for (int j = 0; j < 1; j++)
            {
                SpawnScientist(chinook, config.GuardSettings.GetRandom(), chinook.transform.position - (chinook.transform.forward * 5f), position);
            }

            ClearFireBalls(position);

            MessageAll("EventStart");
        }

        void SpawnScientist(CH47HelicopterAIController chinook, GuardSetting settings, Vector3 position, Vector3 eventPos)
        {
            NPCPlayerApex npc = InstantiateEntity(position, chinook.scientistPrefab.resourcePath);
            if (npc == null)
            {
                return;
            }

            npc.Spawn();

            npc.IsInvinsible = false;
            npc.RadioEffect = new GameObjectRef();
            npc.startHealth = settings.Health;
            npc.InitializeHealth(settings.Health, settings.Health);
            npc.CommunicationRadius = 0;
            npc.displayName = settings.Name;
            npc.Stats.AggressionRange = settings.MaxRange;
            npc.Stats.DeaggroRange = settings.MaxRange * 1.125f;
            npc.Stats.MaxRoamRange = settings.MaxRoamRadius;
            npc.InitFacts();

            (npc as Scientist).LootPanelName = settings.Name;

            npcs.Add(npc);

            npc.Mount((BaseMountable)chinook);

            npc.Invoke(() => {
                GiveKit(npc, settings.KitEnabled, settings.KitName);

                npc.gameObject.AddComponent<GuardDestination>().TargetPoint = GetRandomPoint(eventPos, 5f);
            }, 1f);
        }

        void GiveKit(NPCPlayerApex npc, bool kitEnabled, string kitName)
        {
            if (kitEnabled)
            {
                npc.inventory.Strip();

                Interface.Oxide.CallHook("GiveKit", npc, kitName);
            }
            else
            {
                ItemManager.CreateByName("scientistsuit_heavy", 1, 0)?.MoveToContainer(npc.inventory.containerWear);
            }
        }

        NPCPlayerApex InstantiateEntity(Vector3 position, string prefabName)
        {
            GameObject prefab = GameManager.server.FindPrefab(prefabName);
            GameObject go = Facepunch.Instantiate.GameObject(prefab, position, default(Quaternion));
            go.name = prefabName;

            SceneManager.MoveGameObjectToScene(go, Rust.Server.EntityScene);

            if (go.GetComponent<Spawnable>())
            {
                UnityEngine.GameObject.Destroy(go.GetComponent<Spawnable>());
            }

            if (!go.activeSelf)
            {
                go.SetActive(true);
            }

            return go.GetComponent<NPCPlayerApex>();
        }

        void RemoveNPC(NPCPlayerApex npc)
        {
            if (!npcs.Contains(npc)) return;

            npcs.Remove(npc);

            if (npcs.Count == 0)
            {
                MessageAll("EventEnded");
            }
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
            foreach(CH47LandingZone zone in zones)
            {
                UnityEngine.GameObject.Destroy(zone.gameObject);
            }

            zones.Clear();
        }

        void ClearGuards()
        {
            foreach(NPCPlayerApex npc in npcs)
            {
                if (npc != null && !npc.IsDestroyed)
                {
                    npc.Kill();
                }
            }

            npcs.Clear();
        }

        void ClearFireBalls(Vector3 position)
        {
            List<FireBall> fireBalls = new List<FireBall>();

            Vis.Entities(position, 20f, fireBalls);

            foreach (FireBall fireBall in fireBalls)
            {
                if (fireBall == null || fireBall.IsDestroyed) continue;

                fireBall.Kill();
            }
        }

        void CheckLandingPoint()
        {
            foreach (MonumentInfo monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                if (!monument.gameObject.name.Contains("launch_site_1")) continue;

                hasLaunch = true;

                landingRotation = monument.transform.rotation;
                landingPosition = monument.transform.position + monument.transform.right * 125f;
                landingPosition.y += 5f;

                chinookPosition = monument.transform.position + -monument.transform.right * 250f;
                chinookPosition.y += 150f;
            };
        }
        #endregion

        #region Classes
        class GuardDestination : MonoBehaviour
        {
            NPCPlayerApex npc;
            public Vector3 TargetPoint;

            void Awake()
            {
                npc = gameObject.GetComponent<NPCPlayerApex>();
                if (npc == null)
                {
                    Destroy(this);

                    return;
                }

                InvokeRepeating(nameof(Relocate), 0f, 5f);
            }

            void OnDestroy()
            {
                if (npc != null && !npc.IsDestroyed)
                {
                    npc.Kill();
                }

                CancelInvoke();

                Destroy(this);
            }

            void Relocate()
            {
                float distance = Vector3.Distance(transform.position, TargetPoint);

                if (npc == null || npc.isMounted || !npc.NavAgent.isActiveAndEnabled)
                {
                    return;
                }

                if (npc.AttackTarget == null || npc.AttackTarget != null && distance > npc.Stats.MaxRoamRange)
                {
                    if (npc.IsStuck)
                    {
                        Warp();
                    }

                    if (npc.GetNavAgent == null || !npc.GetNavAgent.isOnNavMesh)
                        npc.finalDestination = TargetPoint;
                    else
                        npc.GetNavAgent.SetDestination(TargetPoint);

                    npc.IsStopped   = false;
                    npc.Destination = TargetPoint;
                }
            }

            public void Warp()
            {
                npc.Pause();
                npc.ServerPosition = TargetPoint;
                npc.GetNavAgent.Warp(TargetPoint);
                npc.stuckDuration = 0f;
                npc.IsStuck = false;
                npc.Resume();
            }
        }
        #endregion

        #region Helpers
        string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        Vector3 GetRandomPoint(Vector3 position, float radius)
        {
            Vector3 vector = position + UnityEngine.Random.onUnitSphere * radius;

            vector.y = TerrainMeta.HeightMap.GetHeight(vector);

            return vector;
        }

        void MessageAll(string key)
        {
            foreach(BasePlayer player in BasePlayer.activePlayerList.Where(x => x.IsConnected))
            {
                player.ChatMessage(Lang(key, player.UserIDString));
            }
        }
        #endregion
    }
}