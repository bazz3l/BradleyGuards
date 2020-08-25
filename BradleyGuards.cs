using System.Collections.Generic;
using System;
using Rust;
using Oxide.Core.Plugins;
using Oxide.Core;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Bradley Guards", "Bazz3l", "1.2.2")]
    [Description("Calls reinforcements when bradley is destroyed at launch site.")]
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
        Vector3 monumentPosition;
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
                ChatIcon = 0,
                APCHealth = 1000f,
                APCCrates = 4,
                NPCAmount = 6,
                InstantCrates = true,
                GuardSettings = new List<GuardSetting> {
                    new GuardSetting("Heavy Gunner", 300f)
                }
            };
        }

        class PluginConfig
        {
            [JsonProperty(PropertyName = "ChatIcon (chat icon SteamID64)")]
            public ulong ChatIcon;

            [JsonProperty(PropertyName = "APCHealth (set starting health)")]
            public float APCHealth;

            [JsonProperty(PropertyName = "APCCrates (amount of crates to spawn)")]
            public int APCCrates;

            [JsonProperty(PropertyName = "NPCAmount (amount of guards to spawn max 11)")]
            public int NPCAmount;

            [JsonProperty(PropertyName = "InstantCrates (unlock crates when guards are eliminated)")]
            public bool InstantCrates;

            [JsonProperty(PropertyName = "GuardSettings (create different types of guards must contain atleast 1)")]
            public List<GuardSetting> GuardSettings;
        }

        class GuardSetting
        {
            [JsonProperty(PropertyName = "Name (custom display name)")]
            public string Name;

            [JsonProperty(PropertyName = "Health (set starting health)")]
            public float Health = 100f;

            [JsonProperty(PropertyName = "DamageScale (higher the value more damage)")]
            public float DamageScale = 0.2f;

            [JsonProperty(PropertyName = "MaxRoamRadius (max radius guards will roam)")]
            public float MaxRoamRadius;

            [JsonProperty(PropertyName = "MaxAggressionRange (distance guards will become aggressive)")]
            public float MaxAggressionRange = 200f;

            [JsonProperty(PropertyName = "KitName (custom kit name)")]
            public string KitName = "";

            [JsonProperty(PropertyName = "KitEnabled (enable custom kit)")]
            public bool KitEnabled = false;

            public GuardSetting(string name, float health, float maxRoamRadius = 50f, float maxAggressionRange = 150f)
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
                {"EventStart", "<color=#DC143C>Bradley Guards</color>: Guards are on route, be prepared to fight or run for your life."},
                {"EventEnded", "<color=#DC143C>Bradley Guards</color>: Guards down, get to the loot."},
            }, this);
        }

        void OnServerInitialized() => GetLandingPoint();

        void Init()
        {
            config = Config.ReadObject<PluginConfig>();
        }

        void Unload() => CleanUp();

        void OnEntitySpawned(BradleyAPC bradley) => OnAPCSpawned(bradley);

        void OnEntityDeath(BradleyAPC bradley, HitInfo info) => OnAPCDeath(bradley);

        void OnEntityDeath(NPCPlayerApex npc, HitInfo info) => OnNPCDeath(npc);

        void OnFireBallDamage(FireBall fire, NPCPlayerApex npc, HitInfo info)
        {
            if (!npcs.Contains(npc))
            {
                return;
            }

            info.damageTypes = new DamageTypeList();
            info.DoHitEffects = false;
            info.damageTypes.ScaleAll(0f);
        }

        void OnEntityDismounted(BaseMountable mountable, NPCPlayerApex npc)
        {
            if (!npcs.Contains(npc))
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
        void SpawnEvent()
        {
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

            for (int i = 0; i < config.NPCAmount; i++)
            {
                SpawnScientist(chinook, config.GuardSettings.GetRandom(), chinook.transform.position + (chinook.transform.forward * 10f), bradleyPosition);
            }

            for (int j = 0; j < 1; j++)
            {
                SpawnScientist(chinook, config.GuardSettings.GetRandom(), chinook.transform.position - (chinook.transform.forward * 5f), bradleyPosition);
            }

            MessageAll("EventStart");
        }

        void SpawnScientist(CH47HelicopterAIController chinook, GuardSetting settings, Vector3 position, Vector3 eventPos)
        {
            NPCPlayerApex npc = GameManager.server.CreateEntity(chinook.scientistPrefab.resourcePath, position, default(Quaternion)) as NPCPlayerApex;
            if (npc == null)
            {
                return;
            }

            npc.Spawn();

            npc.Mount((BaseMountable)chinook);

            npc.RadioEffect = new GameObjectRef();
            npc.CommunicationRadius = 0;
            npc.IsInvinsible = false;
            npc.displayName = settings.Name;
            npc.damageScale = settings.DamageScale;
            npc.startHealth = settings.Health;
            npc.Stats.VisionRange = settings.MaxAggressionRange + 2f;
            npc.Stats.DeaggroRange = settings.MaxAggressionRange + 3f;
            npc.Stats.AggressionRange = settings.MaxAggressionRange + 1f;
            npc.Stats.LongRange = settings.MaxAggressionRange;
            npc.Stats.MaxRoamRange = settings.MaxRoamRadius;
            npc.Stats.Hostility = 1f;
            npc.Stats.Defensiveness = 1f;
            npc.Stats.OnlyAggroMarkedTargets = true;
            npc.InitializeHealth(settings.Health, settings.Health);
            npc.InitFacts();

            (npc as Scientist).LootPanelName = settings.Name;

            npcs.Add(npc);

            npc.Invoke(() => {
                GiveKit(npc, settings.KitEnabled, settings.KitName);

                npc.gameObject.AddComponent<GuardDestination>().TargetPoint = GetRandomPoint(eventPos, 6f);
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

        void OnNPCDeath(NPCPlayerApex npc)
        {
            if (!npcs.Contains(npc))
            {
                return;
            }

            npcs.Remove(npc);

            if (npcs.Count > 0)
            {
                return;
            }

            if (config.InstantCrates)
            {
                RemoveFlames();
                UnlockCrates();
            }

            MessageAll("EventEnded");
        }

        void OnAPCSpawned(BradleyAPC bradley)
        {
            Vector3 pos = bradley.transform.position;

            if (!IsInBounds(pos))
            {
                return;
            }

            bradley.maxCratesToSpawn = config.APCCrates;
            bradley.startHealth      = config.APCHealth;
            bradley.InitializeHealth(config.APCHealth, config.APCHealth);

            ClearGuards();
        }

        void OnAPCDeath(BradleyAPC bradley)
        {
            Vector3 pos = bradley.transform.position;

            if (!IsInBounds(pos))
            {
                return;
            }

            bradleyPosition = pos;

            SpawnEvent();
        }

        void UnlockCrates()
        {
            List<LockedByEntCrate> items = new List<LockedByEntCrate>();

            Vis.Entities(bradleyPosition, 25f, items);

            foreach (LockedByEntCrate item in items)
            {
                item.SetLocked(false);
            }
        }

        void RemoveFlames()
        {
            List<FireBall> items = new List<FireBall>();

            Vis.Entities(bradleyPosition, 25f, items);

            foreach(FireBall item in items)
            {
                item.Kill();
            }
        }

        void CreateLandingZone()
        {
            landingZone = new GameObject(landingName) {
                layer = 16,
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
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                if (!monument.gameObject.name.Contains("launch_site_1")) continue;

                SetLandingPoint(monument);
            };
        }

        void SetLandingPoint(MonumentInfo monument)
        {
            monumentPosition = monument.transform.position;

            landingRotation = monument.transform.rotation;
            landingPosition = monument.transform.position + monument.transform.right * 125f;
            landingPosition.y += 5f;

            chinookRotation = landingRotation;
            chinookPosition = monument.transform.position + -monument.transform.right * 250f;
            chinookPosition.y += 150f;

            hasLaunch = true;

            CreateLandingZone();
        }

        bool IsInBounds(Vector3 position)
        {
            return hasLaunch && Vector3.Distance(monumentPosition, position) <= 300f;
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

                if (npc.AttackTarget == null || (npc.AttackTarget != null && Vector3.Distance(transform.position, TargetPoint) > npc.Stats.MaxRoamRange))
                {
                    if (npc.IsStuck)
                    {
                        DoWarp();
                    }

                    npc.NeverMove = true;

                    if (npc.GetNavAgent == null || !npc.GetNavAgent.isOnNavMesh)
                        npc.finalDestination = TargetPoint;
                    else
                        npc.GetNavAgent.SetDestination(TargetPoint);

                    npc.IsStopped = false;
                    npc.Destination = TargetPoint;
                }
                else
                {
                    npc.NeverMove = false;
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

        void MessageAll(string key)
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                Player.Message(player, Lang(key, player.UserIDString), config.ChatIcon);
            }
        }

        Vector3 GetRandomPoint(Vector3 position, float radius)
        {
            Vector3 pos = position + UnityEngine.Random.onUnitSphere * radius;

            pos.y = TerrainMeta.HeightMap.GetHeight(pos);

            return pos;
        }
        #endregion
    }
}