using System.Collections.Generic;
using System.Collections;
using System;
using Oxide.Core.Plugins;
using Oxide.Core;
using Rust;
using UnityEngine;
using Newtonsoft.Json;
using VLB;
using Pool = Facepunch.Pool;


namespace Oxide.Plugins
{
    [Info("Bradley Guards", "Bazz3l", "1.3.5")]
    [Description("Chinook will fly in and drop off guards to protect the bradley loot when destroyed")]
    public class BradleyGuards : RustPlugin
    {
        [PluginReference] Plugin Kits;
        
        /*
         * TODO Make sure landing zone is clear otherwise find point close for chinook to land
         */

        #region Fields

        private const string ScientistPrefab = "assets/prefabs/npc/scientist/scientist.prefab";
        private const string Ch47Prefab = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
        private const string LandingName = "BradleyLandingZone";
        
        private readonly List<EventManager> GuardEvents = new List<EventManager>();
        private PluginConfig _config;
        private static BradleyGuards Instance;
        
        #endregion

        #region Config
        
        protected override void LoadDefaultConfig() => Config.WriteObject(GetDefaultConfig(), true);

        private PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                InstantCrates = true,
                ChatIcon = 0,
                ApcHealth = 1000f,
                ApcCrateAmount = 3,
                EventTiers = new List<EventTier> {
                    new EventTier
                    {
                        EventName = "Heavy Gunners",
                        GuardName = "Heavy Gunner",
                        GuardHealth = 300f,
                        GuardRoamRadius = 50f,
                        GuardAggressionRange = 150f,
                        GuardAmount = 10
                    },
                    new EventTier
                    {
                        EventName = "Light Gunners",
                        GuardName = "Light Gunner",
                        GuardHealth = 150f,
                        GuardRoamRadius = 50f,
                        GuardAggressionRange = 150f,
                        GuardAmount = 6
                    }
                }
            };
        }

        private class PluginConfig
        {
            [JsonProperty(PropertyName = "InstantCrates (unlock crates when guards are eliminated)")]
            public bool InstantCrates;
            
            [JsonProperty(PropertyName = "ChatIcon (chat icon SteamID64)")]
            public ulong ChatIcon;

            [JsonProperty(PropertyName = "ApcHealth (set starting health)")]
            public float ApcHealth;
            
            [JsonProperty(PropertyName = "ApcCrateAmount (amount of crates to spawn)")]
            public int ApcCrateAmount;

            [JsonProperty(PropertyName = "EventTiers (different types of events that can spawn)")]
            public List<EventTier> EventTiers;
        }

        public class EventTier
        {
            [JsonProperty(PropertyName = "EventName (set the event name to display when event starts)")]
            public string EventName;

            [JsonProperty(PropertyName = "GuardHealth (health of guard)")]
            public float GuardHealth = 100f;

            [JsonProperty(PropertyName = "GuardName (name for guard)")]
            public string GuardName = "";
            
            [JsonProperty(PropertyName = "GuardDamageScale (damage scale of guard)")]
            public float GuardDamageScale = 0.2f;

            [JsonProperty(PropertyName = "GuardRoamRadius (max radius guard will roam)")]
            public float GuardRoamRadius;

            [JsonProperty(PropertyName = "GuardAggressionRange (distance a guard will become aggressive)")]
            public float GuardAggressionRange = 200f;

            [JsonProperty(PropertyName = "KitName (custom kit name)")]
            public string GuardKitName = "";

            [JsonProperty(PropertyName = "KitEnabled (enable custom kit)")]
            public bool GuardKitEnabled = false;

            [JsonProperty(PropertyName = "GuardAmount (amount of guards to spawn max 11)")]
            public int GuardAmount;
        }
        
        #endregion

        #region Oxide
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> {
                {"EventStart", "<color=#DC143C>Bradley Guards</color>: {0} in route, defend the loot."},
                {"EventEnded", "<color=#DC143C>Bradley Guards</color>: All guards down loot up fast."},
            }, this);
        }

        private void Init()
        {
            Instance = this;

            _config = Config.ReadObject<PluginConfig>();
        }

        private void Unload() => StopEvents();

        private void OnEntitySpawned(BradleyAPC bradley) => OnApcSpawned(bradley);

        private void OnEntityDeath(BradleyAPC bradley, HitInfo info) => OnApcDeath(bradley);

        private void OnEntityDeath(Scientist npc, HitInfo info) => OnNpcDeath(npc);

        private void OnEntityKill(Scientist npc) => OnNpcDeath(npc);

        private void OnFireBallDamage(FireBall fire, Scientist npc, HitInfo info)
        {
            if (info == null || !(info.Initiator is FireBall))
            {
                return;
            }

            info.damageTypes = new DamageTypeList();
            info.DoHitEffects = false;
            info.damageTypes.ScaleAll(0f);
        }

        private void OnEntityDismounted(BaseMountable mountable, Scientist npc)
        {
            EventManager npcEvent = EventManager.FindEvent(npc);
            if (npcEvent == null)
            {
                return;
            }

            OnNpcDismount(npc);
        }
        
        #endregion

        #region Core
        
        public class EventManager
        {
            private readonly List<Scientist> NpcApex = new List<Scientist>();
            private readonly List<Vector3> Waypoints = new List<Vector3>();
            private CH47HelicopterAIController Chinook;
            public CH47LandingZone LandingZone;
            public EventTier Settings;
            public Vector3 EventPosition;
            public Quaternion EventRotation;
            public bool InstantCrates;

            public static EventManager FindEvent(Scientist npc) => Instance.GuardEvents.Find(x => x. NpcApex.Contains(npc));

            public void StartEvent()
            {
                CreateLanding();
                CreateWaypoints();
                SpawnChinook(CreateSpawn());

                Instance.GuardEvents.Add(this);

                Instance.MessageAll("EventStart", Settings.EventName);
            }

            public void EndEvent()
            {
                CleanupChinook();
                CleanupLanding();
                CleanupAI();

                if (InstantCrates)
                {
                    Instance.UnlockCrates(EventPosition);
                    Instance.RemoveFlames(EventPosition);
                }

                Instance.GuardEvents.Remove(this);
                
                Instance.MessageAll("EventEnded");
            }

            private void CreateWaypoints()
            {
                for (int i = 0; i < 30; i++)
                {
                    Vector3 position = PositionAround(EventPosition, 5f, i);
                    
                    Waypoints.Add(position);
                }
            }

            private void CreateLanding()
            {
                EventPosition.y = TerrainMeta.HeightMap.GetHeight(EventPosition) + 2f;

                LandingZone = new GameObject(LandingName)
                {
                    layer = 0,
                    transform = {
                        position = EventPosition,
                        rotation = EventRotation
                    }
                }.AddComponent<CH47LandingZone>();
            }

            private void CleanupChinook()
            {
                if (!IsValid(Chinook))
                {
                    return;
                }
                
                Chinook.Kill();
            }
            
            private void CleanupAI()
            {
                List<Scientist> npcList = new List<Scientist>(NpcApex);
                
                foreach (Scientist npc in  npcList)
                {
                    if (!IsValid(npc)) continue;
                    
                    npc.Kill();
                }
                
                npcList.Clear();
            }
            
            private void CleanupLanding()
            {
                if (LandingZone == null)
                {
                    return;
                }
                
                GameObject.DestroyImmediate(LandingZone.gameObject);
            }

            private Vector3 CreateSpawn()
            {
                Vector3 zero = Vector3.zero;
                zero.y = LandingZone.transform.position.y;
                Vector3 vector2d = Vector3Ex.Direction2D(LandingZone.transform.position, zero);
                Vector3 spawnPoint = LandingZone.transform.position + vector2d * 300f;
                spawnPoint.y = TerrainMeta.HeightMap.GetHeight(LandingZone.transform.position) + 200f;

                return spawnPoint;
            }

            private void SpawnChinook(Vector3 position)
            {
                Chinook = GameManager.server.CreateEntity(Ch47Prefab, position, Quaternion.identity) as CH47HelicopterAIController;
                if (Chinook == null)
                {
                    return;
                }

                Chinook.SetLandingTarget(LandingZone.transform.position);
                Chinook.hoverHeight = 2f;
                Chinook.Spawn();
                Chinook.CancelInvoke(new Action(Chinook.SpawnScientists));
                Chinook.GetOrAddComponent<Ch47Component>();
                
                for (int i = 0; i < Settings.GuardAmount - 1; i++)
                {
                    SpawnNpc(Chinook.transform.position + Chinook.transform.forward * 10f);
                }

                for (int j = 0; j < 1; j++)
                {
                    SpawnNpc(Chinook.transform.position - Chinook.transform.forward * 15f);
                }
            }

            private void SpawnNpc(Vector3 position)
            {
                Scientist npc = GameManager.server.CreateEntity(ScientistPrefab, position, Quaternion.identity) as Scientist;
                if (npc == null)
                {
                    return;
                }

                npc.enableSaving = false;
                npc.SetMaxHealth(Settings.GuardHealth);
                npc.Spawn();
                npc.Mount(Chinook);
                
                npc.Stats.OnlyAggroMarkedTargets = false;
                npc.RadioEffect = new GameObjectRef();
                npc.DeathEffect = new GameObjectRef();
                npc.CommunicationRadius = 0;
                npc.displayName = Settings.GuardName;
                npc.LootPanelName = Settings.GuardName;
                npc.damageScale = Settings.GuardDamageScale;
                npc.startHealth = Settings.GuardHealth;
                npc.Stats.MaxRoamRange = Settings.GuardRoamRadius;
                npc.Stats.AggressionRange = Settings.GuardAggressionRange + 1f;
                npc.Stats.VisionRange = npc.Stats.AggressionRange + 2f;
                npc.Stats.DeaggroRange = npc.Stats.AggressionRange + 3f;
                npc.Stats.LongRange = npc.Stats.AggressionRange;
                npc.Stats.Hostility = 1f;
                npc.Stats.Defensiveness = 1f;
                npc.InitFacts();
                npc.GetOrAddComponent<NpcComponent>().SetWaypoint(Waypoints.GetRandom());

                NpcApex.Add(npc);

                npc.Invoke(() => SetupLoadout(npc, Settings.GuardKitEnabled, Settings.GuardKitName), 2f);
            }
            
            public void OnNpcDeath(Scientist npc)
            {
                NpcApex.Remove(npc);

                if (NpcApex.Count > 0)
                {
                    return;
                }

                EndEvent();
            }

            private void SetupLoadout(NPCPlayerApex npc, bool giveKit, string kitName)
            {
                if (!giveKit)
                {
                    ItemManager.CreateByName("scientistsuit_heavy", 1, 0)?.MoveToContainer(npc.inventory.containerWear);
                    return;
                }
                
                npc.inventory.Strip();
            
                Interface.Oxide.CallHook("GiveKit", npc, kitName);                
            }
        }

        private void OnApcSpawned(BradleyAPC bradley)
        {
            bradley.maxCratesToSpawn = _config.ApcCrateAmount;
            bradley.SetHealth(_config.ApcHealth);
            bradley.InitializeHealth(_config.ApcHealth, _config.ApcHealth);
        }

        private void OnApcDeath(BradleyAPC bradley)
        {
            EventManager npcEvent = new EventManager();
            npcEvent.EventPosition = bradley.transform.position;
            npcEvent.EventRotation = bradley.transform.rotation;
            npcEvent.InstantCrates = _config.InstantCrates;
            npcEvent.Settings = _config.EventTiers.GetRandom();
            npcEvent.StartEvent();
        }
        
        private void OnNpcDeath(Scientist npc)
        {
            EventManager npcEvent = EventManager.FindEvent(npc);
            
            npcEvent?.OnNpcDeath(npc);
        }

        private void OnNpcDismount(Scientist npc)
        {
            npc.SetFact(NPCPlayerApex.Facts.IsMounted, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.WantsToDismount, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.CanNotWieldWeapon, (byte) 0, true, true);
            npc.Resume();
        }
        
        private void StopEvents()
        {
            CommunityEntity.ServerInstance.StartCoroutine(DespawnRoutine());
        }
        
        private IEnumerator DespawnRoutine()
        {
            for (int i = GuardEvents.Count - 1; i >= 0; i--)
            {
                EventManager crateEvent = GuardEvents[i];
                
                crateEvent.EndEvent();

                yield return new WaitForSeconds(0.25f);
            }
            
            yield return null;
        }

        private void RemoveFlames(Vector3 position)
        {
            List<FireBall> entities = Pool.GetList<FireBall>();

            Vis.Entities(position, 25f, entities);

            foreach (FireBall fireball in entities)
            {
                if (!IsValid(fireball)) continue;
                
                fireball.Kill();
            }

            Pool.FreeList(ref entities);
        }

        private void UnlockCrates(Vector3 position)
        {
            List<LockedByEntCrate> entities = Pool.GetList<LockedByEntCrate>();

            Vis.Entities(position, 25f, entities);

            foreach (LockedByEntCrate crate in entities)
            {
                if (!IsValid(crate)) continue;
                
                crate.SetLocked(false);
                
                if (crate.lockingEnt == null) continue;

                BaseEntity entity = crate.lockingEnt.ToBaseEntity();

                if (!IsValid(entity)) continue;
                
                entity.Kill();
            }

            Pool.FreeList(ref entities);
        }

        #endregion

        #region Component
        
        private class Ch47Component : MonoBehaviour
        {
            private CH47HelicopterAIController Chinook;

            private void Awake()
            {
                Chinook = gameObject.GetComponent<CH47HelicopterAIController>();

                InvokeRepeating(nameof(Despawn), 5f, 5f);
            }

            private void Despawn()
            {
                if (Chinook == null || Chinook.IsDestroyed || Chinook.HasAnyPassengers())
                {
                    return;
                }

                Chinook.Kill();
            }
        }
        
        private class NpcComponent : MonoBehaviour
        {
            private NPCPlayerApex Npc;
            private Vector3 Destination;

            private void Awake()
            {
                Npc = gameObject.GetComponent<NPCPlayerApex>();

                InvokeRepeating(nameof(Relocate), 5f, 5f);
            }

            private void Relocate()
            {
                if (Npc == null || Npc.IsDestroyed || Npc.isMounted)
                {
                    return;
                }

                if (Npc.AttackTarget == null || Npc.AttackTarget != null && Vector3.Distance(transform.position, Destination) > Npc.Stats.MaxRoamRange)
                {
                    if (Npc.IsStuck)
                    {
                        DoWarp();
                    }

                    if (Npc.GetNavAgent == null || !Npc.GetNavAgent.isOnNavMesh)
                        Npc.finalDestination = Destination;
                    else
                    {
                        Npc.GetNavAgent.SetDestination(Destination);
                        Npc.IsDormant = false;
                    }

                    Npc.IsStopped = false;
                    Npc.Destination = Destination;
                }
            }

            private void DoWarp()
            {
                Npc.Pause();
                Npc.ServerPosition = Destination;
                Npc.GetNavAgent.Warp(Destination);
                Npc.stuckDuration = 0f;
                Npc.IsStuck = false;
                Npc.Resume();
            }

            public void SetWaypoint(Vector3 destination)
            {
                Destination = destination;
            }
        }

        #endregion

        #region Helpers
        
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        private void MessageAll(string key, params object[] args)
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                Player.Message(player, Lang(key, player.UserIDString, args), _config.ChatIcon);
            }
        }

        private static Vector3 PositionAround(Vector3 position, float radius, float angle)
        {
            position.x += radius * Mathf.Sin(angle * Mathf.Deg2Rad);
            position.z += radius * Mathf.Cos(angle * Mathf.Deg2Rad);
            position.y = TerrainMeta.HeightMap.GetHeight(position);
            
            return position;
        }
        
        private static bool IsValid(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return false;
            }

            return true;
        }
        
        #endregion
    }
}