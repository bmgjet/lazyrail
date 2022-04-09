using Rust;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
	[Info("LazyRail", "bmgjet", "0.0.1")]
	[Description("Fix poor quality above ground rail loop created by bmgjets map creator")]
	class LazyRail : RustPlugin
	{
		#region Variables
		public int TrainAmount = 1;
		public bool AllowWorkCarts = true;
		public bool AllowAboveGroundCarts = true;

		public List<BaseEntity> Trains = new List<BaseEntity>();
		public List<Vector3> RailPath = new List<Vector3>();
		private Timer spawner;

		private string WorkCartPrefab = "assets/content/vehicles/workcart/workcart.entity.prefab";
		private string AboveGroundTrainPrefab = "assets/content/vehicles/traintemp/trainenginetemp.entity.prefab";
		#endregion

		#region Commands
		[ChatCommand("lazyrail.spawn")]
		private void ManualSpawn(BasePlayer player, string command, string[] args) { if (player.IsAdmin) {BaseEntity Train = TrainSpawn(player.transform.position, int.Parse(args[0])); if (Train != null) { Trains.Add(Train); } } }
		[ChatCommand("lazyrail.showpath")]
		private void DrawPath(BasePlayer player) { if (player.IsAdmin) { foreach (Vector3 vector in RailPath) { if (Vector3.Distance(vector, player.transform.position) > 400) { continue; } Color c = Color.blue; if (vector.y < TerrainMeta.HeightMap.GetHeight(vector)) { c = Color.red; } player.SendConsoleCommand("ddraw.sphere", 8f, c, vector, 1f); } } }
		#endregion

		#region Hooks
		private void OnServerInitialized()
		{
			ServerMgr.Instance.StartCoroutine(GeneratRailGrid());
			CheckTrains();
			spawner = timer.Every(120, () => { CheckTrains(); });
		}

		private void Unload()
		{
			foreach (BaseEntity be in Trains) { if (be != null && !be.IsDestroyed) be.Kill(); }
			if (spawner != null) { spawner.Destroy(); }
		}

		private object OnEntityTakeDamage(BaseCombatEntity bce, HitInfo info)
		{
			if (bce == null || Trains == null || info == null || bce.net == null) { return null; }
			if (Trains.Contains(bce))
			{
				if (info.damageTypes == null) { return null; }
				if (info.damageTypes.GetMajorityDamageType() == DamageType.Decay) { return false; }
				if (info.damageTypes.GetMajorityDamageType() == DamageType.Collision) { return false; }
				return null;
			}
			return null;
		}

		private void OnEntityDeath(BaseCombatEntity entity, HitInfo info){if (entity != null && info != null) { if (entity is TrainEngine) { if (Trains.Contains(entity)) { Trains.Remove(entity); } } }}
		#endregion

		#region Functions
		private IEnumerator GeneratRailGrid()
		{
			var checks = 0;
			var _instruction = ConVar.FPS.limit > 80 ? CoroutineEx.waitForSeconds(0.01f) : null;
			foreach (PathList pathList in World.GetPaths("Rail").AsEnumerable<PathList>().Reverse<PathList>())
			{
				foreach (Vector3 v in pathList.Path.Points)
				{
					if (++checks >= 1000)
					{
						checks = 0;
						yield return _instruction;
					}
					RailPath.Add(v);
				}
			}
			Puts("Created " + RailPath.Count.ToString() + " Rail Nodes.");
		}

		private BaseEntity setup(BaseEntity baseEntity, string TrainType)
		{
			if (baseEntity == null) return null;
			baseEntity.syncPosition = true;
			baseEntity.globalBroadcast = true;
			baseEntity.enableSaving = false;
			baseEntity.Spawn();
			Puts("Spawned "+TrainType+" @ " + baseEntity.transform.position.ToString());
			TrainEngine te = baseEntity as TrainEngine;
			te.collisionEffect.guid = null;
			te.idleFuelPerSec = 0f;
			te.maxFuelPerSec = 0f;
			NextFrame(() => {te.CancelInvoke("DecayTick");});
			TrainCar tc = baseEntity as TrainCar;
			tc.derailCollisionForce = 0f;
			StorageContainer fuelContainer = te.GetFuelSystem()?.GetFuelContainer();
			if (fuelContainer != null)
			{
				fuelContainer.inventory.AddItem(fuelContainer.allowedItem, 1);
				fuelContainer.SetFlag(BaseEntity.Flags.Locked, true);
			}
			return baseEntity;
		}

		private void CheckTrains()
		{
			if (Rust.Application.isLoading)
			{
				timer.Once(10f, () => { CheckTrains(); });
				return;
			}
			if (RailPath.Count > 5)
			{
				List<Vector3> BasePositions = new List<Vector3>();
				for (int s = 0; s < TrainAmount; s++)
				{
					if (Trains.Count >= TrainAmount) { break; }
					for (int i = 0; i < 100; i++)
					{
						Vector3 SpawnPos = RailPath.GetRandom();
						bool valid = true;
						foreach (Vector3 b in BasePositions)
						{
							if (Vector3.Distance(SpawnPos, b) < 400) { valid = false; break; }
						}
						if (valid)
						{
							BaseEntity train = TrainSpawn(SpawnPos, Random.Range(-1, 2));
							if (train != null)
							{
								Trains.Add(train);
								BasePositions.Add(SpawnPos);
								break;
							}
						}
					}
				}
			}
		}

		private BaseEntity TrainSpawn(Vector3 dropPosition, int Type, string prefab = "", string TrainType = "")
		{
			switch (Type)
			{
				case -1:
				case 0:
					if (AllowWorkCarts){prefab = WorkCartPrefab;}
					break;
				case 1:
				case 2:
					if (AllowAboveGroundCarts){prefab = AboveGroundTrainPrefab;}
					break;
			}
			if(prefab == "")
            {
				if (AllowWorkCarts){prefab = WorkCartPrefab;}
				else if (AllowAboveGroundCarts){prefab = AboveGroundTrainPrefab;}
				else { return null; }
			}
			if(prefab == AboveGroundTrainPrefab){TrainType = "Above Ground Train";}
			else{TrainType = "Workcart";}
			BaseEntity baseEntity = GameManager.server.CreateEntity(prefab, dropPosition, Quaternion.identity, true);
			return setup(baseEntity, TrainType);
		}
		#endregion
	}
}