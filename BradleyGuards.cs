using System.Collections.Generic;
using System;
using Oxide.Core.Plugins;
using Oxide.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Bradley Guards", "Bazz3l", "1.1.4")]
    [Description("Calls for reinforcements when bradley is destroyed.")]
    class BradleyGuards : RustPlugin
    {
        [PluginReference] Plugin Kits;

        #region Fields
        const string ch47Prefab = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
        const string landingName = "BradleyLandingZone";

        HashSet<NPCPlayerApex> npcs = new HashSet<NPCPlayerApex>();
        CH47LandingZone landingZone;
        Quaternion landingRotation;
        Quaternion chinookRotation;
        Vector3 landingPosition;
        Vector3 chinookPosition;
        Vector3 bradleyPosition;
        bool hasLaunch;
        PluginConfig config;
        #endregion

        #region Config
        protected override void LoadDefaultConfig() => Config.WriteObject(GetDefaultConfig(), true);

        PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                CrateAmount = 4,
                DamageScale = 0.6f,
                GuardSettings = new List<GuardSetting> {
                    new GuardSetting("Heavy Gunner", 300f)
                }
            };
        }

        class PluginConfig
        {
            [JsonProperty(PropertyName = "CrateAmount (max amount of crates bradley will spawn)")]
            public int CrateAmount;

            [JsonProperty(PropertyName = "DamageScale (amount of damage scientists should deal)")]
            public float DamageScale;

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

            [JsonProperty(PropertyName = "MaxAggressionRange (max distance they will become agressive)")]
            public float MaxAggressionRange = 100f;

            [JsonProperty(PropertyName = "KitName (custom kit name)")]
            public string KitName = "";

            [JsonProperty(PropertyName = "KitEnabled (enable custom kit)")]
            public bool KitEnabled = false;

            public GuardSetting(string name, float health, float maxRoamRadius = 80f, float maxAggressionRange = 50f)
            {
                Name = name;
                Health = health;
                MaxRoamRadius = maxRoamRadius;
                MaxAggressionRange = maxAggressionRange;
            }
        }
        #endregion

        #region Oxide
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> {
                {"EventStart", "[<color=#DC143C>Bradley Guards</color>]: Guards are on route, be prepared to fight or run for your life."},
                {"EventEnded", "[<color=#DC143C>Bradley Guards</color>]: Guards down, get to the loot."},
            }, this);
        }

        void OnServerInitialized() => GetLandingPoint();

        void Init()
        {
            config = Config.ReadObject<PluginConfig>();
        }

        void Unload() => CleanUp();

        void OnEntitySpawned(BradleyAPC bradley)
        {
            bradley.maxCratesToSpawn = config.CrateAmount;

            ClearGuards();
        }

        void OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            if (info?.Initiator is NPCPlayerApex)
            {
                info.damageTypes.ScaleAll(config.DamageScale);
            }
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

            CH47HelicopterAIController chinook = GameManager.server.CreateEntity(ch47Prefab, chinookPosition, chinookRotation) as CH47HelicopterAIController;
            if (chinook == null)
            {
                return;
            }

            chinook.SetLandingTarget(landingPosition);
            chinook.SetMoveTarget(landingPosition);
            chinook.hoverHeight = 1.5f;
            chinook.Spawn();
            chinook.CancelInvoke(new Action(chinook.SpawnScientists));

            for (int i = 0; i < 11; i++)
            {
                SpawnScientist(chinook, config.GuardSettings.GetRandom(), chinook.transform.position + (chinook.transform.forward * 10f), position);
            }

            for (int j = 0; j < 1; j++)
            {
                SpawnScientist(chinook, config.GuardSettings.GetRandom(), chinook.transform.position - (chinook.transform.forward * 5f), position);
            }

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

            npc.RadioEffect = new GameObjectRef();
            npc.CommunicationRadius = 0;
            npc.IsInvinsible = false;
            npc.displayName = settings.Name;
            npc.Stats.AggressionRange = settings.MaxAggressionRange;
            npc.Stats.DeaggroRange = settings.MaxRange * 1f;
            npc.Stats.MaxRoamRange = settings.MaxRoamRadius;
            npc.startHealth = settings.Health;
            npc.InitializeHealth(settings.Health, settings.Health);
            npc.InitFacts();

            (npc as Scientist).LootPanelName = settings.Name;

            npcs.Add(npc);

            npc.Mount((BaseMountable)chinook);

            npc.Invoke(() => {
                GiveKit(npc, settings.KitEnabled, settings.KitName);

                npc.gameObject.AddComponent<GuardDestination>().TargetPoint = GetRandomPoint(eventPos, 5f);
            }, 2f);
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
            if (!npcs.Contains(npc))
            {
                return;
            }

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
            if (landingZone != null)
            {
                UnityEngine.GameObject.Destroy(landingZone.gameObject);
            }

            landingZone = null;
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

        void GetLandingPoint()
        {
            foreach (MonumentInfo monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                if (!monument.gameObject.name.Contains("launch_site_1")) continue;

                landingRotation = monument.transform.rotation;
                landingPosition = monument.transform.position + monument.transform.right * 125f;
                landingPosition.y += 5f;

                chinookRotation = landingRotation;
                chinookPosition = monument.transform.position + -monument.transform.right * 250f;
                chinookPosition.y += 150f;

                SetLandingPoint();
            };
        }

        void SetLandingPoint()
        {
            hasLaunch = true;

            landingZone = CreateLandingZone();
        }
        #endregion

        #region Component
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
            }

            void Relocate()
            {
                if (npc == null || npc.isMounted)
                {
                    return;
                }

                float distance = Vector3.Distance(transform.position, TargetPoint);

                if (npc.AttackTarget == null || npc.AttackTarget != null && distance > npc.Stats.MaxRoamRange)
                {
                    if (npc.IsStuck)
                    {
                        DoWarp();
                    }

                    if (npc.GetNavAgent == null || !npc.GetNavAgent.isOnNavMesh)
                        npc.finalDestination = TargetPoint;
                    else
                        npc.GetNavAgent.SetDestination(TargetPoint);

                    npc.IsStopped   = false;
                    npc.Destination = TargetPoint;
                }
            }

            public void DoWarp()
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
            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                player.ChatMessage(Lang(key, player.UserIDString));
            }
        }
        #endregion
    }
}