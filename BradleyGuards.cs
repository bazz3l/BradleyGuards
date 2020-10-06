using System.Collections.Generic;
using System.Linq;
using System;
using Rust;
using Oxide.Core.Plugins;
using Oxide.Core;
using UnityEngine;
using Facepunch;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Bradley Guards", "Bazz3l", "1.3.2")]
    [Description("Calls reinforcements when bradley is destroyed at launch site.")]
    public class BradleyGuards : RustPlugin
    {
        [PluginReference] Plugin Kits;

        #region Fields
        
        private const string Ch47Prefab = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
        private const string LandingName = "BradleyLandingZone";

        private readonly HashSet<NPCPlayerApex> npcs = new HashSet<NPCPlayerApex>();
        private CH47HelicopterAIController chinook;
        private CH47LandingZone landingZone;
        private Quaternion landingRotation;
        private Quaternion chinookRotation;
        private Vector3 monumentPosition;
        private Vector3 landingPosition;
        private Vector3 chinookPosition;
        private Vector3 bradleyPosition;
        private bool hasLaunch;
        private PluginConfig config;
        
        #endregion

        #region Config
        
        protected override void LoadDefaultConfig() => Config.WriteObject(GetDefaultConfig(), true);

        private PluginConfig GetDefaultConfig()
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

        private class PluginConfig
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

        private class GuardSetting
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

        private void OnServerInitialized() => GetLandingPoint();

        private void Init()
        {
            config = Config.ReadObject<PluginConfig>();
        }

        private void Unload() => CleanUp();

        private void OnEntitySpawned(BradleyAPC bradley) => OnAPCSpawned(bradley);

        private void OnEntityDeath(BradleyAPC bradley, HitInfo info) => OnAPCDeath(bradley);

        private void OnEntityDeath(NPCPlayerApex npc, HitInfo info) => OnNPCDeath(npc);

        private void OnEntityKill(NPCPlayerApex npc) => OnNPCDeath(npc);

        private void OnFireBallDamage(FireBall fire, NPCPlayerApex npc, HitInfo info)
        {
            if (!(npcs.Contains(npc) && info.Initiator is FireBall))
            {
                return;
            }

            info.damageTypes = new DamageTypeList();
            info.DoHitEffects = false;
            info.damageTypes.ScaleAll(0f);
        }

        private void OnEntityDismounted(BaseMountable mountable, NPCPlayerApex npc)
        {
            if (!npcs.Contains(npc))
            {
                return;
            }

            npc.SetFact(NPCPlayerApex.Facts.IsMounted, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.WantsToDismount, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.CanNotWieldWeapon, (byte) 0, true, true);
            npc.modelState.mounted = false;
            npc.Resume();
        }
        
        #endregion

        #region Core
        
        private void SpawnEvent()
        {
            chinook = GameManager.server.CreateEntity(Ch47Prefab, chinookPosition, Quaternion.identity) as CH47HelicopterAIController;
            chinook.SetLandingTarget(landingPosition);
            chinook.hoverHeight = 1.5f;
            chinook.Spawn();
            chinook.CancelInvoke(new Action(chinook.SpawnScientists));
            
            chinook.gameObject.AddComponent<CustomCH47>();

            for (int i = 0; i < config.NPCAmount - 1; i++)
            {
                SpawnScientist(chinook, config.GuardSettings.GetRandom(), chinook.transform.position + chinook.transform.forward * 10f, bradleyPosition);
            }

            for (int j = 0; j < 1; j++)
            {
                SpawnScientist(chinook, config.GuardSettings.GetRandom(), chinook.transform.position - chinook.transform.forward * 15f, bradleyPosition);
            }

            MessageAll("EventStart");
        }

        private void SpawnScientist(CH47HelicopterAIController chinook, GuardSetting settings, Vector3 position, Vector3 eventPos)
        {
            NPCPlayerApex npc = GameManager.server.CreateEntity(chinook.scientistPrefab.resourcePath, position, Quaternion.identity) as NPCPlayerApex;
            npc.Spawn();
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
            npc.Mount(chinook);
            
            npcs.Add(npc);

            npc.Invoke(() => {
                (npc as Scientist).LootPanelName = settings.Name;
                
                GiveKit(npc, settings.KitEnabled, settings.KitName);

                npc.gameObject.AddComponent<CustomNavigation>().SetDestination(GetRandomPoint(eventPos, 6f));
            }, 2f);
        }

        private void GiveKit(NPCPlayerApex npc, bool kitEnabled, string kitName)
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

        private void OnNPCDeath(NPCPlayerApex npc)
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

        private void OnAPCSpawned(BradleyAPC bradley)
        {
            Vector3 pos = bradley.transform.position;
            
            if (!IsInBounds(pos))
            {
                return;
            }

            bradley.maxCratesToSpawn = config.APCCrates;
            bradley.startHealth = config.APCHealth;
            bradley._maxHealth = config.APCHealth;
            
            ClearGuards();
        }

        private void OnAPCDeath(BradleyAPC bradley)
        {
            Vector3 pos = bradley.transform.position;

            if (!IsInBounds(pos))
            {
                return;
            }

            bradleyPosition = pos;

            SpawnEvent();
        }

        private void RemoveFlames()
        {
            List<FireBall> entities = Facepunch.Pool.GetList<FireBall>();

            Vis.Entities(bradleyPosition, 25f, entities);

            foreach (FireBall fireball in entities)
            {
                if (fireball == null || fireball.IsDestroyed) continue;

                NextFrame(() => fireball.Kill());
            }

            Pool.FreeList(ref entities);
        }

        private void UnlockCrates()
        {
            List<LockedByEntCrate> entities = Facepunch.Pool.GetList<LockedByEntCrate>();

            Vis.Entities(bradleyPosition, 25f, entities);

            foreach (LockedByEntCrate crate in entities)
            {
                if (crate != null && crate.IsValid())
                {
                    crate.SetLocked(false);

                    if (crate.lockingEnt == null) continue;

                    BaseEntity entity = crate.lockingEnt.GetComponent<BaseEntity>();

                    if (entity != null && entity.IsValid())
                    {
                        entity.Kill();
                    }
                }
            }

            Pool.FreeList(ref entities);
        }

        private void CreateLandingZone()
        {
            landingZone = new GameObject(LandingName) {
                layer = 16,
                transform = {
                    position = landingPosition,
                    rotation = landingRotation
                }
            }.AddComponent<CH47LandingZone>();
        }

        private void CleanUp()
        {
            ClearGuards();
            ClearZones();
        }

        private void ClearZones()
        {
            if (landingZone != null)
            {
                GameObject.Destroy(landingZone.gameObject);
            }

            landingZone = null;
        }

        private void ClearGuards()
        {
            for (int i = 0; i < npcs.Count; i++)
            {
                NPCPlayerApex npc = npcs.ElementAt(i);
                if (npc != null && !npc.IsDestroyed)
                {
                    npc.Kill();
                }
            }

            npcs.Clear();
        }

        private void GetLandingPoint()
        {
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                if (!monument.gameObject.name.Contains("launch_site_1")) continue;

                SetLandingPoint(monument);
            };
        }

        private void SetLandingPoint(MonumentInfo monument)
        {
            monumentPosition = monument.transform.position;

            landingRotation = monument.transform.rotation;
            landingPosition = monument.transform.position + monument.transform.right * 125f;
            landingPosition.y = TerrainMeta.HeightMap.GetHeight(landingPosition);

            chinookRotation = landingRotation;
            chinookPosition = monument.transform.position + -monument.transform.right * 250f;
            chinookPosition.y += 150f;

            hasLaunch = true;

            CreateLandingZone();
        }

        private bool IsInBounds(Vector3 position)
        {
            return hasLaunch && Vector3.Distance(monumentPosition, position) <= 300f;
        }
        
        #endregion

        #region Component
        
        private class CustomNavigation : MonoBehaviour
        {
            private NPCPlayerApex Npc;
            private Vector3 TargetPoint;

            private void Awake()
            {
                Npc = gameObject.GetComponent<NPCPlayerApex>();
                if (Npc == null)
                {
                    Destroy(this);

                    return;
                }

                InvokeRepeating(nameof(Relocate), 0f, 5f);
            }

            private void OnDestroy()
            {
                if (Npc != null && !Npc.IsDestroyed)
                {
                    Npc.Kill();
                }

                CancelInvoke();
            }

            public void SetDestination(Vector3 position)
            {
                TargetPoint = position;
            }

            private void Relocate()
            {
                if (Npc == null || Npc.isMounted)
                {
                    return;
                }

                if (Npc.AttackTarget == null || (Npc.AttackTarget != null && Vector3.Distance(transform.position, TargetPoint) > Npc.Stats.MaxRoamRange))
                {
                    if (Npc.IsStuck)
                    {
                        DoWarp();
                    }

                    if (Npc.GetNavAgent == null || !Npc.GetNavAgent.isOnNavMesh)
                        Npc.finalDestination = TargetPoint;
                    else
                    {
                        Npc.GetNavAgent.SetDestination(TargetPoint);
                        Npc.IsDormant = false;
                    }

                    Npc.IsStopped = false;
                    Npc.Destination = TargetPoint;
                }
            }

            private void DoWarp()
            {
                Npc.Pause();
                Npc.ServerPosition = TargetPoint;
                Npc.GetNavAgent.Warp(TargetPoint);
                Npc.stuckDuration = 0f;
                Npc.IsStuck = false;
                Npc.Resume();
            }
        }

        private class CustomCH47 : MonoBehaviour
        {
            private CH47HelicopterAIController Chinook;
            private CH47AIBrain Brain;
            private bool Destroying;

            private void Awake()
            {
                Chinook = GetComponent<CH47HelicopterAIController>();
                Brain = GetComponent<CH47AIBrain>();

                InvokeRepeating(nameof(CheckLanded), 5f, 5f);
            }

            private void OnDestroy() => CancelInvoke(nameof(CheckLanded));

            private void CheckLanded()
            {
                if (Chinook == null || Chinook.IsDestroyed || Chinook.HasAnyPassengers())
                {
                    return;
                }

                if (!Destroying && Brain._currentState == 7)
                {
                    Destroying = true;

                    Chinook.Invoke("DelayedKill", 5f);
                }
            }
        }
        
        #endregion

        #region Helpers
        
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        private void MessageAll(string key)
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                Player.Message(player, Lang(key, player.UserIDString), config.ChatIcon);
            }
        }

        private Vector3 GetRandomPoint(Vector3 position, float radius)
        {
            Vector3 pos = position + UnityEngine.Random.onUnitSphere * radius;
            
            pos.y = TerrainMeta.HeightMap.GetHeight(pos);
            
            return pos;
        }
        
        #endregion
    }
}