using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Bradley Guards", "Bazz3l", "1.2.2")]
    [Description("Calls in reinforcements when bradley is taken down")]
    class BradleyGuards : RustPlugin
    {
        [PluginReference] Plugin Kits;

        #region Fields
        const string _lockedPrefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
        const string _ch47Prefab = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
        const string _landingName = "BradleyLandingZone";

        HashSet<CH47LandingZone> _zones = new HashSet<CH47LandingZone>();
        HashSet<NPCPlayerApex> _npcs = new HashSet<NPCPlayerApex>();

        PluginConfig _config;
        Quaternion _landingRotation;
        Vector3 _landingPosition;
        Vector3 _chinookPosition;
        bool _hasLaunch;

        static BradleyGuards plugin;
        #endregion

        #region Config
        protected override void LoadDefaultConfig() => Config.WriteObject(GetDefaultConfig(), true);

        PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                GuardMaxSpawn = 10, // Max is 11
                GuardMaxRoam = 150,
                GuardAggressionRange = 151f,
                GuardVisionRange = 153f,
                GuardLongRange = 150f,
                GuardDeaggroRange = 154f,
                GuardName = "Guard",
                CrateAmount = 4,
                UseKit = false,
                NPCKits = new List<string> {
                    "guard",
                    "guard-heavy"
                }
            };
        }

        class PluginConfig
        {
            public float GuardAggressionRange;
            public float GuardDeaggroRange;
            public float GuardVisionRange;
            public float GuardLongRange;
            public int GuardMaxSpawn;
            public int GuardMaxRoam;
            public string GuardName;
            public int CrateAmount;
            public bool UseKit;
            public List<string> NPCKits;
        }
        #endregion

        #region Oxide
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> {
                {"EventStart", "<color=#DC143C>Bradley Guards</color>: Bradley down, reinforcements flying in prepare to fight."},
                {"EventEnded", "<color=#DC143C>Bradley Guards</color>: Reinforcements down."},
            }, this);
        }

        void OnServerInitialized()
        {
            CheckLandingPoint();

            if (!_hasLaunch) return;

            CH47LandingZone zone = CreateLandingZone();

            _zones.Add(zone);
        }

        void Init()
        {
            plugin = this;
            _config = Config.ReadObject<PluginConfig>();
        }

        void Unload() => CleanUp();

        void OnEntitySpawned(BradleyAPC bradley)
        {
            bradley.maxCratesToSpawn = _config.CrateAmount;
            
            ClearGuards();
        }

        void OnEntityDeath(BradleyAPC bradley)
        {
            if (bradley == null || !_hasLaunch) return;

            SpawnEvent(bradley.transform.position);
        }

        void OnEntityDeath(NPCPlayerApex npc, HitInfo info)
        {
            if (!_npcs.Contains(npc)) return;

            _npcs.Remove(npc);

            if (_npcs.Count == 0)
            {
                MessageAll("EventEnded");
            }
        }

        void OnEntityDismounted(BaseMountable mountable, NPCPlayerApex npc)
        {
            if (npc == null || !_npcs.Contains(npc)) return;

            npc.SetFact(NPCPlayerApex.Facts.IsMounted, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.WantsToDismount, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.CanNotWieldWeapon, (byte) 0, true, true);
            npc.Resume();
        }
        #endregion

        #region Core
        void SpawnEvent(Vector3 eventPos)
        {
            CH47HelicopterAIController chinook = GameManager.server.CreateEntity(_ch47Prefab, _chinookPosition, _landingRotation) as CH47HelicopterAIController;
            if (chinook == null) return;

            chinook.SetLandingTarget(_landingPosition);
            chinook.SetMoveTarget(_landingPosition);
            chinook.hoverHeight = 1.5f;
            chinook.Spawn();
            chinook.CancelInvoke(new Action(chinook.SpawnScientists));

            for (int i = 0; i < _config.GuardMaxSpawn; i++)
            {
                SpawnScientist(chinook, chinook.transform.position + (chinook.transform.forward * 10f), eventPos);
            }

            for (int j = 0; j < 1; j++)
            {
                SpawnScientist(chinook, chinook.transform.position - (chinook.transform.forward * 5f), eventPos);
            }

            MessageAll("EventStart");
        }

        void SpawnScientist(CH47HelicopterAIController chinook, Vector3 position, Vector3 eventPos)
        {
            BaseEntity entity = GameManager.server.CreateEntity(chinook.scientistPrefab.resourcePath, position, Quaternion.identity);

            NPCPlayerApex component = entity.GetComponent<NPCPlayerApex>();
            if (component != null)
            {
                entity.enableSaving = false;
                entity.Spawn();

                component.CancelInvoke(component.EquipTest);
                component.CancelInvoke(component.RadioChatter);
                component.startHealth = 100f;
                component.InitializeHealth(component.startHealth, component.startHealth);
                component.RadioEffect           = new GameObjectRef();
                component.CommunicationRadius   = 0;
                component.displayName           = _config.GuardName;
                component.Stats.AggressionRange = _config.GuardAggressionRange;
                component.Stats.VisionRange     = _config.GuardVisionRange;
                component.Stats.DeaggroRange    = _config.GuardDeaggroRange;
                component.Stats.LongRange       = _config.GuardLongRange;
                component.Stats.MaxRoamRange    = _config.GuardMaxRoam;
                component.Stats.Hostility       = 1;
                component.Stats.Defensiveness   = 1;
                component.InitFacts();
                component.Mount((BaseMountable)chinook);
                component.gameObject.AddComponent<BradleyGuard>()?.Init(plugin._config.GuardMaxRoam, RandomCircle(eventPos, 10));

                _npcs.Add(component);

                timer.In(1f, () => GiveKit(component, _config.NPCKits.GetRandom(), _config.UseKit));
            }
            else
            {
                entity.Kill(BaseEntity.DestroyMode.None);
            }
        }

        void GiveKit(NPCPlayerApex npc, string kitName, bool give)
        {
            if (!give) return;

            npc.inventory.Strip();

            Interface.Oxide.CallHook("GiveKit", npc, kitName);
        }

        CH47LandingZone CreateLandingZone()
        {
            return new GameObject(_landingName) {
                layer     = 16, 
                transform = { 
                    position = _landingPosition, 
                    rotation = _landingRotation 
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
            foreach(CH47LandingZone zone in _zones)
            {
                UnityEngine.GameObject.Destroy(zone.gameObject);
            }

            _zones.Clear();
        }

        void ClearGuards()
        {
            foreach(NPCPlayerApex npc in _npcs)
            {
                if (npc != null && !npc.IsDestroyed)
                {
                    npc?.Kill();
                }
            }

            _npcs.Clear();
        }

        void CheckLandingPoint()
        {
            foreach (MonumentInfo monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                if (!monument.gameObject.name.Contains("launch_site_1")) continue;

                _hasLaunch = true;          

                _landingRotation = monument.transform.rotation;
                _landingPosition = monument.transform.position + monument.transform.right * 125f;
                _landingPosition.y += 5f;

                _chinookPosition = monument.transform.position + -monument.transform.right * 250f;
                _chinookPosition.y += 150f;
            };
        }
        #endregion

        #region Classes
        class BradleyGuard : MonoBehaviour
        {
            NPCPlayerApex _npc;
            Vector3 _targetDestination;
            float _maxRoamDistance;

            public void Init(float maxRoamDistance, Vector3 targetDestination)
            {
                _maxRoamDistance    = maxRoamDistance;
                _targetDestination  = targetDestination;
                _npc.ServerPosition = targetDestination;
            }

            void Awake()
            {
                _npc = gameObject.GetComponent<NPCPlayerApex>();
                if (_npc == null)
                {
                    Destroy(this);
                    return;
                }

                if (gameObject.GetComponent<Spawnable>()) Destroy(gameObject.GetComponent<Spawnable>());
            }

            void FixedUpdate() => ShouldRelocate();

            void OnDestroy()
            {
                if (_npc == null || _npc.IsDestroyed) return;

                _npc?.Kill();
            }

            void ShouldRelocate()
            {
                if (_npc == null || _npc.IsDestroyed || _npc.isMounted) return;

                float distance = Vector3.Distance(transform.position, _targetDestination);

                bool moveback = distance >= 10 || distance >= _maxRoamDistance;

                if (_npc.AttackTarget == null && moveback || _npc.AttackTarget != null && moveback)
                {
                    if (_npc.GetNavAgent == null || !_npc.GetNavAgent.isOnNavMesh)
                        _npc.finalDestination = _targetDestination;
                    else
                        _npc.GetNavAgent.SetDestination(_targetDestination);

                    _npc.Destination = _targetDestination;
                    _npc.SetFact(NPCPlayerApex.Facts.Speed, moveback ? (byte)NPCPlayerApex.SpeedEnum.Sprint : (byte)NPCPlayerApex.SpeedEnum.Walk, true, true);
                }
            }
        }
        #endregion

        #region Helpers
        string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        static Vector3 RandomCircle(Vector3 center, float radius)
        {
            float angle = UnityEngine.Random.Range(0f, 100f) * 360;
            Vector3 pos = center;
            pos.x += radius * Mathf.Sin(angle * Mathf.Deg2Rad);
            pos.z += radius * Mathf.Cos(angle * Mathf.Deg2Rad);
            return pos;
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

