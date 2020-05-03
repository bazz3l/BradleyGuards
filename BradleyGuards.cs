using System.Collections.Generic;
using System;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Bradley Guards", "Bazz3l", "1.1.9")]
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

        public static BradleyGuards plugin;
        #endregion

        #region Config
        protected override void LoadDefaultConfig() => Config.WriteObject(GetDefaultConfig(), true);

        PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                GuardMaxSpawn = 11, // Max is 11
                GuardMaxRoam = 30,
                GuardAggressionRange = 151f,
                GuardVisionRange = 153f,
                GuardLongRange = 150f,
                GuardDeaggroRange = 154f,
                GuardDamageScale = 0.5f,
                GuardName = "Guard",
                GuardKit = "guard"
            };
        }

        class PluginConfig
        {
            public float GuardAggressionRange;
            public float GuardDeaggroRange;
            public float GuardVisionRange;
            public float GuardLongRange;
            public float GuardDamageScale;
            public int GuardMaxSpawn;
            public int GuardMaxRoam;
            public string GuardName;
            public string GuardKit;
        }
        #endregion

        #region Oxide
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> {
                {"EventStart", "<color=#DC143C>Bradley Guards</color>: reinforcements inbound."},
                {"EventEnded", "<color=#DC143C>Bradley Guards</color>: reinforcements down loot up fast."},
            }, this);
        }

        void OnServerInitialized()
        {
            CheckLandingPoint();

            if (_hasLaunch)
            {
                CH47LandingZone zone = CreateLandingZone();

                _zones.Add(zone);
            }
        }

        void Init()
        {
            plugin = this;
            _config = Config.ReadObject<PluginConfig>();
        }

        void Unload() => CleanUp();

        void OnEntitySpawned(BradleyAPC bradley) => ClearGuards();

        void OnEntityDeath(BradleyAPC bradley)
        {
            if (bradley == null || !_hasLaunch)
            {
                return;
            }

            SpawnEvent(bradley.transform.position);
        }

        void OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            if (info == null || info?.Initiator == null || !(info?.Initiator is NPCPlayerApex))
            {
                return;
            }

            NPCPlayerApex npc = info.Initiator as NPCPlayerApex;

            if (npc == null || !_npcs.Contains(npc))
            {
                return;
            }

            info.damageTypes.ScaleAll(_config.GuardDamageScale);
        }

        void OnEntityDeath(NPCPlayerApex npc, HitInfo info)
        {
            if (npc == null || !_npcs.Contains(npc))
            {
                return;
            }

            _npcs.Remove(npc);

            if (_npcs.Count == 0)
            {
                MessageAll("EventEnded");
            }
        }

        void OnEntityDismounted(BaseMountable mountable, NPCPlayerApex npc)
        {
            if (npc == null || !_npcs.Contains(npc))
            {
                return;
            }

            npc.SetFact(NPCPlayerApex.Facts.CanNotWieldWeapon, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.WantsToDismount, (byte) 0, true, true);
            npc.SetFact(NPCPlayerApex.Facts.IsMounted, (byte) 0, true, true);
            npc.Resume();
        }
        #endregion

        #region Core
        void SpawnEvent(Vector3 eventPos)
        {
            CH47HelicopterAIController chinook = GameManager.server.CreateEntity(_ch47Prefab, _chinookPosition, _landingRotation) as CH47HelicopterAIController;
            if (chinook == null)
            {
                return;
            }

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

        void SpawnScientist(CH47HelicopterAIController chinook, Vector3 spawnPos, Vector3 eventPos)
        {
            NPCPlayerApex npc = GameManager.server.CreateEntity(chinook.scientistPrefab.resourcePath, spawnPos, Quaternion.identity) as NPCPlayerApex;
            npc.Spawn();
            npc.Mount((BaseMountable)chinook);
            npc.RadioEffect = new GameObjectRef();
            npc.DeathEffect = new GameObjectRef();
            npc.displayName = _config.GuardName;
            npc.Stats.AggressionRange = _config.GuardAggressionRange;
            npc.Stats.VisionRange = _config.GuardVisionRange;
            npc.Stats.DeaggroRange = _config.GuardDeaggroRange;
            npc.Stats.LongRange = _config.GuardLongRange;
            npc.Stats.Hostility = 1;
            npc.Stats.Defensiveness = 1;
            npc.InitFacts();
            npc.gameObject.AddComponent<BradleyGuard>().eventCenter = eventPos;

            _npcs.Add(npc);

            Interface.Oxide.CallHook("GiveKit", npc, _config.GuardKit);
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
                if (!monument.gameObject.name.Contains("launch_site_1"))
                {
                    continue;
                }

                _hasLaunch = true;                

                _landingRotation = monument.transform.rotation;
                _landingPosition = monument.transform.position + monument.transform.right * 125f;
                _landingPosition.y += 5f;

                _chinookPosition = monument.transform.position + -monument.transform.right * 125f;
                _chinookPosition.y += 150f;
            };
        }
        #endregion

        #region Classes
        class BradleyGuard : MonoBehaviour
        {
            NPCPlayerApex _npc;
            Vector3 _spawnPosition;
            bool _moveBack;
            public Vector3 eventCenter;

            void Start()
            {
                _npc = gameObject.GetComponent<NPCPlayerApex>();

                if (_npc == null)
                {
                    Destroy(this);
                    return;
                }

                _spawnPosition = plugin.RandomCircle(eventCenter, 10f);
            }

            void FixedUpdate() => ShouldRelocate();

            void OnDestroy()
            {
                if (_npc == null || _npc.IsDestroyed)
                {
                    return;
                }

                _npc?.Kill();
            }

            void ShouldRelocate()
            {
                if (_npc == null || _npc.IsDestroyed || _npc.isMounted)
                {
                    return;
                }

                float distance  = Vector3.Distance(transform.position, eventCenter);
                bool shouldMove = (!IsAggro() && distance >= 10 || IsAggro() && distance >= plugin._config.GuardMaxRoam);

                if(!_moveBack && shouldMove)
                {
                    _moveBack = true;
                }

                if (_moveBack && shouldMove)
                {
                    if (_npc.IsNavRunning())
                        _npc.GetNavAgent.SetDestination(_spawnPosition);
                    else
                        _npc.finalDestination = _spawnPosition;
                }
                else
                {
                    _moveBack = false;
                }
            }

            bool IsAggro()
            {
                return _npc.GetFact(NPCPlayerApex.Facts.IsAggro) != (byte) 0;
            }
        }
        #endregion

        #region Helpers
        string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        Vector3 RandomCircle(Vector3 center, float radius)
        {
            float angle = UnityEngine.Random.Range(0f, 100f) * 360;
            Vector3 pos = center;
            pos.x += radius * Mathf.Sin(angle * Mathf.Deg2Rad);
            pos.z += radius * Mathf.Cos(angle * Mathf.Deg2Rad);
            return pos;
        }

        void MessageAll(string key)
        {
            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected)
                {
                    continue;
                }

                player.ChatMessage(Lang(key, player.UserIDString));
            }
        }
        #endregion
    }
}
