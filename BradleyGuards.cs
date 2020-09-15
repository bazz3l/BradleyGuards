using System.Collections.Generic;
using System;
using Rust;
using Oxide.Core.Plugins;
using Oxide.Core;
using UnityEngine;
using Facepunch;
using Newtonsoft.Json;
using VLB;

namespace Oxide.Plugins
{
    [Info("Bradley Guards", "Bazz3l", "1.3.1")]
    [Description("Call reinforcements to bradley.")]
    public class BradleyGuards : RustPlugin
    {
        [PluginReference] Plugin Kits;

        #region Fields
        private const string ChairPrefab = "assets/bundled/prefabs/static/chair.invisible.static.prefab";
        private const string ChutePrefab = "assets/prefabs/misc/parachute/parachute.prefab";
        private const string ScientistPrefab = "assets/prefabs/npc/scientist/scientistjunkpile.prefab";
        private const string Ch47Prefab = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
        private const string LandingName = "BradleyLandingZone";
        
        private readonly BradleyEventManager _manager = new BradleyEventManager();
        private static PluginConfig _config;
        private static BradleyGuards _instance;
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
                ToolTip = true,
                InstantCrates = true,
                GuardSettings = new List<GuardSetting> {
                    new GuardSetting("Australian Gunner", 300f),
                    new GuardSetting("Swedish Gunner", 300f),
                    new GuardSetting("Russian Gunner", 300f),
                    new GuardSetting("British Gunner", 300f)
                }
            };
        }

        private class PluginConfig
        {
            [JsonProperty(PropertyName = "ChatIcon (chat icon SteamID64)")]
            public ulong ChatIcon;
            
            [JsonProperty(PropertyName = "ToolTip (enable tool tip messages)")]
            public bool ToolTip;

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
            public float MaxAggressionRange = 150f;

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

        private void Init()
        {
            _instance = this;
            
            _config = Config.ReadObject<PluginConfig>();
        }

        private void Unload() => _manager.ClearEvents();

        private void OnEntitySpawned(BradleyAPC bradley) => OnAPCSpawned(bradley);

        private void OnEntityDeath(BradleyAPC bradley, HitInfo info) => OnAPCDeath(bradley);

        private void OnEntityDeath(Scientist npc, HitInfo info) => OnNPCDeath(npc);

        private void OnEntityKill(Scientist npc) => OnNPCDeath(npc);

        private void OnFireBallDamage(FireBall fire, Scientist npc, HitInfo info) => OnNPCFireball(npc, info);

        private void OnEntityDismounted(BaseMountable mountable, Scientist npc) => OnNPCDismount(npc);
        #endregion

        #region Core
        private void OnAPCSpawned(BradleyAPC bradley)
        {
            bradley.maxCratesToSpawn = _config.APCCrates;
            bradley._maxHealth = _config.APCHealth;
            bradley.health = bradley._maxHealth;
        }

        private void OnAPCDeath(BradleyAPC bradley)
        {
            Vector3 pos = bradley.transform.position;

            _manager.AddEvent(pos);
        }
        
        private void OnNPCDeath(Scientist npc)
        {
            BradleyEvent bradleyEvent = _manager.BradleyEvents.Find(x => x.NpcPlayers.Contains(npc));

            bradleyEvent?.OnNPCDeath(npc);
        }

        private void OnNPCFireball(Scientist npc, HitInfo info)
        {
            BradleyEvent bradleyEvent = _manager.BradleyEvents.Find(x => x.NpcPlayers.Contains(npc));
            if (!(bradleyEvent != null && info.Initiator is FireBall))
            {
                return;
            }
            
            info.damageTypes = new DamageTypeList();
            info.DoHitEffects = false;
            info.damageTypes.ScaleAll(0f);
        }

        private void OnNPCDismount(Scientist npc)
        {
            BradleyEvent bradleyEvent = _manager.BradleyEvents.Find(x => x.NpcPlayers.Contains(npc));
            if (bradleyEvent == null)
            {
                return;
            }

            GiveChute(npc);
        }

        private void GiveChute(Scientist npc)
        {
            if (npc == null)
            {
                return;
            }
            
            BaseEntity mount = GameManager.server.CreateEntity(ChairPrefab, npc.transform.position, Quaternion.identity, true);
            mount.enableSaving = false;
            
            var hasstab = mount.GetComponent<StabilityEntity>();
            if (hasstab) hasstab.grounded = true;
            
            var hasmount = mount.GetComponent<BaseMountable>();
            if (hasmount) hasmount.isMobile = true;
            
            mount.skinID = 1311472987;
            mount?.Spawn();
            
            if (mount != null)
            {
                BaseEntity parachute = GameManager.server.CreateEntity(ChutePrefab, new Vector3(), new Quaternion(), true);
                parachute.SetParent(mount, 0);
                parachute?.Spawn();

                mount.gameObject.GetOrAddComponent<CustomChute>();
                
                hasmount.MountPlayer(npc);
            }
        }
        
        private static void RemoveFlames(Vector3 position)
        {
            List<FireBall> entities = Facepunch.Pool.GetList<FireBall>();

            Vis.Entities(position, 25f, entities);

            foreach (FireBall fireball in entities)
            {
                if (fireball == null || fireball.IsDestroyed) continue;

                _instance.NextFrame(() => fireball.Kill());
            }

            Pool.FreeList(ref entities);
        }

        private static void UnlockCrates(Vector3 position)
        {
            List<LockedByEntCrate> entities = Facepunch.Pool.GetList<LockedByEntCrate>();

            Vis.Entities(position, 25f, entities);

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
        #endregion

        #region Event
        private class BradleyEventManager
        {
            public readonly List<BradleyEvent> BradleyEvents = new List<BradleyEvent>();

            public void AddEvent(Vector3 position)
            {
                BradleyEvent bradleyEvent = new BradleyEvent();
                bradleyEvent.PrepEvent(position, _config);
                BradleyEvents.Add(bradleyEvent);
                bradleyEvent.StartEvent();
            }

            public void RemoveEvent(BradleyEvent bradleyEvent) => BradleyEvents.Remove(bradleyEvent);

            public void ClearEvents()
            {
                foreach (BradleyEvent bradleyEvent in BradleyEvents.ToArray())
                {
                    bradleyEvent.StopEvent();
                }
                
                BradleyEvents.Clear();
            }
        }

        private class BradleyEvent
        {
            private const float ChinookHoverHeight = 50f;
            public readonly ListHashSet<Scientist> NpcPlayers = new ListHashSet<Scientist>();
            private CH47HelicopterAIController _chinook;
            private CH47LandingZone _landingZone;
            private GuardSetting _guardSettings;
            private Vector3 _landingPos;
            private Vector3 _eventPos;
            private bool _instantCrates;
            private int _maxGuards;

            public void StartEvent()
            {
                _landingZone = CreateLanding();

                _chinook = GameManager.server.CreateEntity(Ch47Prefab, new Vector3(0,0,0), new Quaternion(), true) as CH47HelicopterAIController;
                if (_chinook == null)
                {
                    return;
                }

                _chinook.transform.position = _landingPos + (Vector3.up * 80);
                _chinook.SetLandingTarget(_landingPos);
                _chinook.hoverHeight = ChinookHoverHeight;
                _chinook.Spawn();
                _chinook.CancelInvoke(new Action(_chinook.SpawnScientists));
                _chinook.gameObject.AddComponent<CustomCH47>();

                SpawnPassengers();

                _instance.MessageAll("EventStart");
            }

            public void StopEvent()
            {
                if (_instantCrates)
                {
                    UnlockCrates(_eventPos);
                    RemoveFlames(_eventPos);                    
                }

                Cleanup();

                _instance.MessageAll("EventEnded");
            }

            public void PrepEvent(Vector3 position, PluginConfig config)
            {
                _eventPos = position;
                
                position.y = TerrainMeta.HeightMap.GetHeight(position) + ChinookHoverHeight;
                
                _landingPos = position;
                _maxGuards = config.NPCAmount;
                _instantCrates = config.InstantCrates;
                _guardSettings = config.GuardSettings.GetRandom();
                
                _instance.Puts("Position: {0}, Guards: {1}, Unlock: {2}", _eventPos, _maxGuards, _instantCrates);
            }
            
            private void Cleanup()
            {
                // Destroy chinook
                if (_chinook != null && !_chinook.IsDestroyed)
                {
                    _chinook.Kill();
                }
                
                // Destroy npcs
                foreach (Scientist npc in NpcPlayers)
                {
                    if (npc != null && !npc.IsDestroyed)
                    {
                        npc.Kill();
                    }
                }

                // Destroy landing zone
                if (_landingZone != null)
                {
                    UnityEngine.Object.DestroyImmediate(_landingZone.gameObject);
                }

                // Lastly remove event
                _instance._manager.RemoveEvent(this);
            }
            
            public void OnNPCDeath(Scientist npc)
            {
                NpcPlayers.Remove(npc);

                if (NpcPlayers.Count > 0)
                {
                    return;
                }

                StopEvent();
            }

            private void SpawnPassengers()
            {
                for (var i = 0; i < _maxGuards - 1; i++)
                {
                    CreateScientist(i, _chinook.transform.position + _chinook.transform.forward * 10f);
                }
                
                for (int j = 0; j < 1; j++)
                {
                    CreateScientist(j, _chinook.transform.position - _chinook.transform.forward * 15f);
                }
            }
            
            private CH47LandingZone CreateLanding()
            {
                return new GameObject(LandingName) {
                    layer = 0,
                    transform = { position = _landingPos, rotation = Quaternion.identity }
                }.AddComponent<CH47LandingZone>();
            }

            private void CreateScientist(int seat, Vector3 position)
            {
                Scientist npc = (Scientist)GameManager.server.CreateEntity(ScientistPrefab, position, default(Quaternion), true);
                npc.Spawn();
                npc.Mount((BaseMountable) _chinook);
                
                NpcPlayers.Add(npc);

                npc.RadioEffect = new GameObjectRef();
                npc.IsInvinsible = false;
                npc.CommunicationRadius = -1f;
                npc.MaxDistanceToCover = -1f;
                npc.LootPanelName = _guardSettings.Name;
                npc.displayName = _guardSettings.Name;
                npc.damageScale = _guardSettings.DamageScale;
                npc.startHealth = _guardSettings.Health;
                npc.Stats.VisionRange = _guardSettings.MaxAggressionRange + 2f;
                npc.Stats.DeaggroRange = _guardSettings.MaxAggressionRange + 3f;
                npc.Stats.AggressionRange = _guardSettings.MaxAggressionRange + 1f;
                npc.Stats.LongRange = _guardSettings.MaxAggressionRange;
                npc.Stats.MaxRoamRange = _guardSettings.MaxRoamRadius;
                npc.Stats.Hostility = 1f;
                npc.Stats.Defensiveness = 1f;
                npc.Stats.HealthThresholdFleeChance = 1f;
                npc.Stats.OnlyAggroMarkedTargets = false;
                npc.InitializeHealth(_guardSettings.Health, _guardSettings.Health);
                npc.InitFacts();

                npc.Invoke(() => GiveLoadout(npc, _guardSettings.KitEnabled, _guardSettings.KitName), 0.2f);
            }
        }
        #endregion
        
        #region Component
        private class CustomCH47 : MonoBehaviour
        {
            private CH47HelicopterAIController _chinook;
            private CH47AIBrain _brain;
            private bool _destroy;

            private void Awake()
            {
                _chinook = GetComponent<CH47HelicopterAIController>();
                _brain = GetComponent<CH47AIBrain>();

                InvokeRepeating(nameof(CheckLanded), 5f, 5f);
            }

            private void OnDestroy() => CancelInvoke(nameof(CheckLanded));

            private void CheckLanded()
            {
                if (_chinook == null || _chinook.IsDestroyed || _chinook.HasAnyPassengers())
                {
                    return;
                }

                if (!_destroy && _brain._currentState == 7)
                {
                    _destroy = true;

                    _chinook.Invoke("DelayedKill", 5f);
                }
            }
        }
        
        private class CustomChute : MonoBehaviour
        {
            private readonly int _layerMask = LayerMask.GetMask("Terrain", "Construction", "World", "Deployable");
            private BaseMountable _mountable;
            private BaseEntity _chute;

            private void Awake()
            {
                _chute = GetComponentInParent<BaseEntity>();
                _mountable = _chute.GetComponent<BaseMountable>();
                
                if (_chute == null)
                {
                    OnDestroy();
                }
            }

            private void Update()
            {
                if (!IsMounted() || _chute == null)
                {
                    OnDestroy();
                    return;
                }
                
                RaycastHit hit;
                
                if (Physics.Raycast(_chute.transform.position, Vector3.down, out hit, 2f, _layerMask))
                {
                    OnDestroy();
                    return;
                }

                _chute.transform.position = Vector3.MoveTowards(_chute.transform.position, _chute.transform.position + Vector3.down, 5f * Time.deltaTime);
                _chute.transform.hasChanged = true;
                _chute.SendNetworkUpdateImmediate();
                _chute.UpdateNetworkGroup();
            }

            private void OnDestroy()
            {
                if (_chute != null && !_chute.IsDestroyed)
                {
                    _chute.Kill();
                }

                Scientist npc = _mountable.GetMounted() as Scientist;
                if (npc == null)
                {
                    return;
                }
                
                npc.SetFact(NPCPlayerApex.Facts.IsMounted, (byte) 0, true, true);
                npc.SetFact(NPCPlayerApex.Facts.WantsToDismount, (byte) 0, true, true);
                npc.SetFact(NPCPlayerApex.Facts.CanNotWieldWeapon, (byte) 0, true, true);
                npc.modelState.mounted = false;
                npc.Resume();
            }
            
            bool IsMounted()
            {
                return _chute.GetComponent<BaseMountable>()?.IsMounted() == true;
            }
        }
        #endregion

        #region Helpers
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        private void MessageAll(string key)
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (_config.ToolTip)
                    ToolTip(player, Lang(key, player.UserIDString));
                else
                    Player.Message(player, Lang(key, player.UserIDString), _config.ChatIcon);
            }
        }
        
        private void ToolTip(BasePlayer player, string message, float time = 5f)
        {
            if (player == null)
            {
                return;
            }
            
            player.SendConsoleCommand("gametip.showgametip", message);
            
            timer.Once(time, () => player?.SendConsoleCommand("gametip.hidegametip"));
        }
        
        private static void GiveLoadout(Scientist npc, bool kitEnabled, string kitName)
        {
            npc.inventory.Strip();

            if (kitEnabled)
                Interface.Oxide.CallHook("GiveKit", npc, kitName);
            else
            {
                ItemManager.CreateByName("rifle.lr300", 1, 0)?.MoveToContainer(npc.inventory.containerBelt);
                ItemManager.CreateByName("scientistsuit_heavy", 1, 0)?.MoveToContainer(npc.inventory.containerWear);
            }
        }
        #endregion
    }
}