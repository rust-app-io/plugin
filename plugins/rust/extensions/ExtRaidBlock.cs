
using Rust;
using Pool = Facepunch.Pool;
using System;
using Layer = Rust.Layer;
using Time = UnityEngine.Time;
using Oxide.Core;
using UnityEngine;
using VLB;
using System.Collections.Generic;
using System.Linq;
using Facepunch.Extend;

namespace Oxide.Plugins
{
	[Info("ExtRaidBlock", "rustapp.io (by Bizlich)", "1.0.0")]
	public class ExtRaidBlock : RustPlugin
	{
        public int RaidBlockDistance = 150; // Зона рейдблока
        public int RaidBlockDuration = 15; // Время рейдблока
        public bool RaidBlockOnEnterRaidZone = true; // Вешать рб при заходе в зону
        public bool RaidBlockOnExitRaidZone = true; // Снимать рб при выходе из зоны
        public bool RaidBlockAddedAllPlayersInZoneRaid = true; // Вешать всем рб в зоне рейда
        public bool RaidBlockOnPlayerDeath = true; // Снимать рб после смерти

        private bool IsRaidBlock(ulong userId) => IsRaidBlocked(userId);
        private bool IsRaidBlocked(BasePlayer player) => IsBlocked(player);
        private bool IsRaidBlocked(string playerId)
        {
            BasePlayer target = BasePlayer.Find(playerId);
            return target != null && IsBlocked(target);
        }
        private bool IsBlockedClass(BaseCombatEntity entity) => entity is BuildingBlock or Door or SimpleBuildingBlock or Workbench or Barricade or BasePlayer;
        private RaidPlayer GetRaidPlayer(BasePlayer player)
        {
            RaidPlayer obj = player.GetComponent<RaidPlayer>();
            return obj;
        }
        private Dictionary<uint, string> _prefabID2Item = new();
        private readonly Dictionary<BasePlayer, Timer> _playerTimer = new Dictionary<BasePlayer, Timer>();
        private List<RaidableZone> _raidZoneComponents = new(); 
        public List<DamageType> _damageTypes = new();
        public static ExtRaidBlock Instance;
        private Dictionary<string, string> _prefabNameItem = new()
        {
            ["40mm_grenade_he"] = "multiplegrenadelauncher",
            ["grenade.beancan.deployed"] = "grenade.beancan",
            ["grenade.f1.deployed"] = "grenade.f1",
            ["explosive.satchel.deployed"] = "explosive.satchel",
            ["explosive.timed.deployed"] = "explosive.timed",
            ["rocket_basic"] = "ammo.rocket.basic",
            ["rocket_hv"] = "ammo.rocket.hv",
            ["rocket_fire"] = "ammo.rocket.fire",
            ["survey_charge.deployed"] = "surveycharge"
        }; 		  						  	   		  	 	 		  	   		  	 				  	 	 
        private void CreateOrRefreshRaidblock(Vector3 position, BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, Name)) 
                return;
            if (Interface.Call("CanRaidBlock", player, position) != null)
                return;
            RaidableZone raidableZone = _raidZoneComponents != null && _raidZoneComponents.Count != 0 ? _raidZoneComponents.FirstOrDefault(x => x != null && Vector3.Distance(position, x.transform.position) < RaidBlockDistance) : null;
            if (raidableZone != null)
                raidableZone.RefreshTimer(position, player);
            else
            {
                raidableZone = new GameObject().AddComponent<RaidableZone>();
                _raidZoneComponents.Add(raidableZone);
                raidableZone.CreateRaidZone(position, player);
            }
            Interface.CallHook("OnRaidBlock", position);
        } 		 		  						  	   		  	 	 		  	   		  	 				  	 	 
        private void OnPlayerDeath(BasePlayer player, HitInfo hitInfo)
        {
            if (player == null || hitInfo == null || !player.userID.IsSteamId())
                return;
            RaidPlayer raidPlayer = GetRaidPlayer(player);
            if (raidPlayer != null)
            {
                UnityEngine.Object.DestroyImmediate(raidPlayer);
            }
        }
        [ChatCommand("rbtest")]
        void rbtest(BasePlayer player)
        {
            if(!player.IsAdmin) return;
            CreateOrRefreshRaidblock(player.transform.position, player);
        }     
        private void CheckUnsubscribeOrSubscribeHooks()
        {
            if (_raidZoneComponents.Count == 0)
                UnsubscribeHook(false, true);
            else
                SubscribeHook(false, true);
        }
        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info) => OnEntCheck(entity, info);
        private void OnEntCheck(BaseCombatEntity entity, HitInfo info)
        {
            DamageType? majorityDamageType = info?.damageTypes.GetMajorityDamageType();
            if (majorityDamageType == DamageType.Decay)
                return;
            BasePlayer raider = info?.InitiatorPlayer ? info.InitiatorPlayer : entity.lastAttacker as BasePlayer;
            if(raider == null) return;
            if (IsBlockedClass(entity))
            {  						  	   		  	 	 		  	   		  	 				  	 	 
                if (CheckEntity(entity, info, raider))
                {		  						  	   		  	 	 		  	   		  	 				  	 	 
                    CreateOrRefreshRaidblock(entity.transform.position, raider);
                }
            }
        }
        private bool IsRaidBlocked(ulong playerId)
        {
            BasePlayer target = BasePlayer.Find(playerId.ToString());
            return target != null && IsBlocked(target);
        }
        private void UnsubscribeHook(bool main, bool raidActions)
        {
            if (main)
            {
                Unsubscribe(nameof(OnEntityDeath));
            }
            if (raidActions)
            {
                Unsubscribe(nameof(OnPlayerDeath));
            }
        }
        private List<RaidPlayer> raidPlayersList = new();
        private void OnServerInitialized()
        {
            SubscribeHook(true, false);
            foreach (ItemDefinition itemDef in ItemManager.GetItemDefinitions())
            {
                Item newItem = ItemManager.CreateByName(itemDef.shortname);
                BaseEntity heldEntity = newItem.GetHeldEntity();
                if (heldEntity != null)
                {
                    _prefabID2Item[heldEntity.prefabID] = itemDef.shortname;
                }
                if (itemDef.TryGetComponent(out ItemModDeployable itemModDeployable) && itemModDeployable.entityPrefab != null)
                {
                    string deployablePrefab = itemModDeployable.entityPrefab.resourcePath;
                    if (!string.IsNullOrEmpty(deployablePrefab))
                    {
                        GameObject prefab = GameManager.server.FindPrefab(deployablePrefab);
                        if (prefab != null && prefab.TryGetComponent(out BaseEntity baseEntity))
                        {
                            string shortPrefabName = baseEntity.ShortPrefabName;
                            if (!string.IsNullOrEmpty(shortPrefabName))
                            {
                                _prefabNameItem.TryAdd(shortPrefabName, itemDef.shortname);
                            }
                        }
                    }
                }
                newItem.Remove();
            }
        }
        private void Init()
        {
            Instance = this;
            UnsubscribeHook(true, true);
        }
        private static RaidableZone GetRbZone(Vector3 position)
        {
            List<SphereCollider> sphereColliders = new ();
            Vis.Colliders(position, 0.1f, sphereColliders);
            if (sphereColliders.Count <= 0) return null;
            foreach (SphereCollider sCollider in sphereColliders)
            {
                if (!sCollider.gameObject.TryGetComponent(out RaidableZone rbZone)) continue;
                return rbZone;
            }
            return null;
        }
        private void Unload()
        {
            foreach (RaidableZone obj in _raidZoneComponents) 
                UnityEngine.Object.DestroyImmediate(obj);
            foreach (RaidPlayer rPlayer in raidPlayersList)
                if(rPlayer != null)
                    rPlayer.Kill(true);
            Instance = null;
        }
        void OnPlayerEnteredRaidableBase(BasePlayer player, Vector3 raidPos, bool allowPVP, int mode)
        {
            RaidPlayer raidPlayer = player.GetOrAddComponent<RaidPlayer>();
            raidPlayer.ActivateBlock(Time.realtimeSinceStartup);
            Interface.CallHook("OnEnterRaidZone", player);
        }
        void OnPlayerExitedRaidableBase(BasePlayer player, Vector3 raidPos, bool allowPVP, int mode)
        {
            RaidPlayer rp = GetRaidPlayer(player);
            if (rp != null)
            {
                UnityEngine.Object.DestroyImmediate(rp);

                Interface.CallHook("OnExitRaidZone", player);
            }
            RaidableZone rbZone = GetRbZone(player.transform.position);
            if (rbZone == null) return;
            rbZone.AddPlayer(player);
        }
        private void SubscribeHook(bool main, bool raidActions)
        {
            if (main)
            {
                Subscribe(nameof(OnEntityDeath));		 		  						  	   		  	 	 		  	   		  	 				  	 	
            }

            if (raidActions)
            {
                if (RaidBlockOnPlayerDeath)
                {
                    Subscribe(nameof(OnPlayerDeath));
                }
            }
        }      
        private bool IsBlocked(BasePlayer player)
        {
            RaidPlayer obj = player.GetComponent<RaidPlayer>();
            return obj != null && obj.UnblockTimeLeft > 0;
        }				  	   		  	 	 		  	   		  	 				  	 	 
        void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null || player.IsDead() || !player.IsConnected) return;
            RaidableZone rbZone = GetRbZone(player.transform.position);
            if (rbZone == null) return;
            rbZone.AddPlayer(player);
        }
        private bool CheckEntity(BaseCombatEntity entity, HitInfo info, BasePlayer raider)
        {
            if (entity.OwnerID == 0 || !entity.OwnerID.IsSteamId())
                return false;
            if (entity is BuildingBlock { grade: BuildingGrade.Enum.Twigs } block) return false;
            return true;
        }
        private class RaidableZone : MonoBehaviour
        {            
            private Dictionary<BasePlayer, RaidPlayer> _playersAndComponentZone = new();
            private SphereCollider triggerZone;
            private BasePlayer initiatorRaid;
            private Single timeToUnblock;
            public Single UnblockTimeLeft => Convert.ToInt32(timeToUnblock - Time.realtimeSinceStartup);
            private Int32 raidBlockDistance;
            private Int32 raidBlockDuration;
            private void InitializeTriggerZone()
            {
                triggerZone = gameObject.AddComponent<SphereCollider>();
                triggerZone.radius = raidBlockDistance;
                triggerZone.gameObject.layer = (int) Layer.Reserved1;
                triggerZone.transform.SetParent(transform, true);
                triggerZone.isTrigger = true;
            }
		   		 		  						  	   		  	 	 		  	   		  	 				  	 	 
            public void CreateRaidZone(Vector3 raidPos, BasePlayer initiator)
            {
                Instance.CheckUnsubscribeOrSubscribeHooks();
                transform.position = raidPos;
                initiatorRaid = initiator;
                InitializeTriggerZone();
                AddPlayer(initiatorRaid, true);
                if (Instance.RaidBlockAddedAllPlayersInZoneRaid) 
                    AddAllPlayerInZoneDistance();
                Interface.CallHook("OnCreatedRaidZone", raidPos, initiator);
            }
            public void RefreshTimer(Vector3 pos, BasePlayer initiatorReply = null)
            {
                timeToUnblock = Time.realtimeSinceStartup + raidBlockDuration;
                CancelInvoke(nameof(EndRaid));
                Invoke(nameof(EndRaid), raidBlockDuration);
                if (initiatorReply != null && !_playersAndComponentZone.ContainsKey(initiatorReply))
                {
                    AddPlayer(initiatorReply);
                }
                foreach (KeyValuePair<BasePlayer, RaidPlayer> playerAndComponent in _playersAndComponentZone)
                {
                    RaidPlayer raidPlayer = playerAndComponent.Value;
                    if (raidPlayer != null)
                    {
                        if (Vector3.Distance(playerAndComponent.Key.transform.position,
                            triggerZone.transform.position) >= raidBlockDistance)
                        {
                            Instance.NextTick(() =>
                            {
                                _playersAndComponentZone.Remove(playerAndComponent.Key);
                            });
                            continue;
                        }
                        raidPlayer.UpdateTime(timeToUnblock);
                    }
                }
            }
            private void EndRaid() 
            {
                Interface.CallHook("OnRaidBlockStopped", transform.position);
                Instance._raidZoneComponents.Remove(this);
                Instance.CheckUnsubscribeOrSubscribeHooks();
                Destroy(this);
            }
            private void AddAllPlayerInZoneDistance() 
            {
                List<BasePlayer> players = Pool.GetList<BasePlayer>();
                Vis.Entities(transform.position, raidBlockDistance, players);
                foreach (BasePlayer player in players)
                {
                    if(_playersAndComponentZone.ContainsKey(player))
                        continue;
                    AddPlayer(player, true);
                }
                Pool.FreeList(ref players);
            }
            public void AddPlayer(BasePlayer player, Boolean force = false)
            {
                if (!Instance.RaidBlockOnEnterRaidZone && !force) return;
                RaidPlayer raidPlayer = player.GetOrAddComponent<RaidPlayer>();
                Single leftTimeZone = raidPlayer.UnblockTimeLeft > UnblockTimeLeft
                    ? raidPlayer.UnblockTimeLeft
                    : UnblockTimeLeft;
                if (player == initiatorRaid)
                {
                    initiatorRaid = null;
                }
                _playersAndComponentZone[player] = raidPlayer;
                raidPlayer.ActivateBlock(timeToUnblock);
                Interface.CallHook("OnEnterRaidZone", player);
            }
            public void RemovePlayer(BasePlayer player)
            {
                if(!_playersAndComponentZone.ContainsKey(player)) return;
                RaidPlayer raidPlayer = _playersAndComponentZone[player];
                if (raidPlayer == null) return;
                if (Instance.RaidBlockOnExitRaidZone)
                {
                    Destroy(_playersAndComponentZone[player]);
                }
                _playersAndComponentZone.Remove(player);
                player.Invoke(() => RecheackRbZone(player), 0.1f);
                Interface.CallHook("OnExitRaidZone", player);
            }
            public void RecheackRbZone(BasePlayer player)
            {
                if (player == null || player.IsDead() || !player.IsConnected) return;
                RaidableZone rbZone = GetRbZone(player.transform.position);
                if (rbZone == null) return;
                rbZone.AddPlayer(player);
            }

            private void Awake()
            {
                raidBlockDistance = Instance.RaidBlockDistance;
                raidBlockDuration = Instance.RaidBlockDuration;
                RefreshTimer(transform.position);
            }
            private void OnDestroy()
            { 
                Destroy(triggerZone);
            }
            private void OnTriggerEnter(Collider collider)
            {
                BasePlayer player = collider.GetComponentInParent<BasePlayer>();
                if (player != null && player.net?.connection!=null)
                {
                    AddPlayer(player);
                }
            }
            private void OnTriggerExit(Collider collider)
            {
                BasePlayer player = collider.GetComponentInParent<BasePlayer>();
                if (player != null && player.net?.connection!=null)
                {
                    RemovePlayer(player);
                }
            }
        }
        private class RaidPlayer : FacepunchBehaviour
        {
            public BasePlayer player;
            public Single blockEnds;
            public float UnblockTimeLeft => Convert.ToInt32(blockEnds - Time.realtimeSinceStartup);
            private void Awake()
            {
                player = GetComponent<BasePlayer>();
                if(!Instance.raidPlayersList.Contains(this))
                    Instance.raidPlayersList.Add(this);
                Interface.CallHook("OnRaidBlockStarted", player);
            }
            public void Kill(Boolean force = false)
            {
                if (!force)
                {
                    if (Instance.raidPlayersList.Contains(this))
                        Instance.raidPlayersList.Remove(this);
                }
                DestroyImmediate(this);
            }
            private void OnDestroy()
            {
                Interface.CallHook("OnRaidBlockStopped", player);
            }       
            public void UpdateTime(Single time, Boolean customTime = false)
            {
                if (customTime)
                {
                    blockEnds = time;
                    return;
                }
                if (time > blockEnds)
                    blockEnds = time;
            }
            public void ActivateBlock(Single time)
            {
                if(time > blockEnds)
                    blockEnds = time;
                InvokeRepeating(CheckTimeLeft, 0, 1);
            }
            private void CheckTimeLeft()
            {
                if (UnblockTimeLeft > 0){}
                else
                {   
                    CancelInvoke(nameof(CheckTimeLeft));
                    Kill();
                }
            }        
        } 
        object RustApp_IsInRaid(ulong userId) 
        {
            bool IsRaid = IsRaidBlocked(userId);
            return IsRaid;
        }
	}
}

        