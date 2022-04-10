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
		public bool AllowWorkCarts = false;
		public bool AllowAboveGroundCarts = true;

		public List<BaseEntity> Trains = new List<BaseEntity>();
		public List<Vector3> RailPath = new List<Vector3>();
		private Timer spawner;
		public static LazyRail plugin;

		private string WorkCartPrefab = "assets/content/vehicles/workcart/workcart.entity.prefab";
		private string AboveGroundTrainPrefab = "assets/content/vehicles/traintemp/trainenginetemp.entity.prefab";
		#endregion

		#region Commands
		[ChatCommand("lazyrail.spawn")]
		private void ManualSpawn(BasePlayer player, string command, string[] args) { if (player.IsAdmin) { BaseEntity Train = TrainSpawn(player.transform.position, int.Parse(args[0])); if (Train != null) { Trains.Add(Train); } } }
		[ChatCommand("lazyrail.showpath")]
		private void DrawPath(BasePlayer player) { if (player.IsAdmin) { foreach (Vector3 vector in RailPath) { if (Vector3.Distance(vector, player.transform.position) > 400) { continue; } Color c = Color.blue; if (vector.y < TerrainMeta.HeightMap.GetHeight(vector)) { c = Color.red; } player.SendConsoleCommand("ddraw.sphere", 8f, c, vector, 1f); } } }
		#endregion

		#region Hooks

		private void Init()
		{
			plugin = this;
		}

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
			plugin = null;
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

		private void OnEntityDeath(BaseCombatEntity entity, HitInfo info) { if (entity != null && info != null) { if (entity is TrainEngine) { if (Trains.Contains(entity)) { Trains.Remove(entity); } } } }
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
			Puts("Spawned " + TrainType + " @ " + baseEntity.transform.position.ToString());
			TrainEngine te = baseEntity as TrainEngine;
			te.collisionEffect.guid = null;
			te.idleFuelPerSec = 0f;
			te.maxFuelPerSec = 0f;
			TrainCar tc = baseEntity as TrainCar;
			tc.derailCollisionForce = 0f;
			StorageContainer fuelContainer = te.GetFuelSystem()?.GetFuelContainer();
			if (fuelContainer != null)
			{
				fuelContainer.inventory.AddItem(fuelContainer.allowedItem, 1);
				fuelContainer.SetFlag(BaseEntity.Flags.Locked, true);
			}
			Lazy Fix = baseEntity.gameObject.AddComponent<Lazy>();
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
					if (AllowWorkCarts) { prefab = WorkCartPrefab; }
					break;
				case 1:
				case 2:
					if (AllowAboveGroundCarts) { prefab = AboveGroundTrainPrefab; }
					break;
			}
			if (prefab == "")
			{
				if (AllowWorkCarts) { prefab = WorkCartPrefab; }
				else if (AllowAboveGroundCarts) { prefab = AboveGroundTrainPrefab; }
				else { return null; }
			}
			if (prefab == AboveGroundTrainPrefab) { TrainType = "Above Ground Train"; }
			else { TrainType = "Workcart"; }
			BaseEntity baseEntity = GameManager.server.CreateEntity(prefab, dropPosition, Quaternion.identity, true);
			return setup(baseEntity, TrainType);
		}
		#endregion

		private class Lazy : MonoBehaviour
		{
			public BaseEntity _train;
			public TrainEngine _trainEngine;
			public TrainCar _trainCar;
			public float CurrentSpeed = 0;

			private void Awake()
			{
				_train = GetComponent<BaseEntity>();
				_trainEngine = _train as TrainEngine;
				_trainCar = _train as TrainCar;
				_trainCar.frontCollisionTrigger.interestLayers = Layers.Mask.Vehicle_World;
				_trainCar.rearCollisionTrigger.interestLayers = Layers.Mask.Vehicle_World;
				plugin.NextFrame(() => { _trainEngine.CancelInvoke("DecayTick"); });
			}

			private void OnDestroy()
			{
				try
				{
					enabled = false;
					CancelInvoke();

					if (_train != null && !_train.IsDestroyed) { _train.Kill(); }
				}
				catch { }
			}

			public void Die() { if (this != null) { Destroy(this); } }

			public void movetrain()
			{
				Vector3 Direction = base.transform.forward;
				TrainTrackSpline preferredAltTrack = (_trainCar.RearTrackSection != _trainCar.FrontTrackSection) ? _trainCar.RearTrackSection : null;
				TrainTrackSpline trainTrackSpline;
				bool flag;
				_trainCar.FrontWheelSplineDist = _trainCar.FrontTrackSection.GetSplineDistAfterMove(_trainCar.FrontWheelSplineDist, Direction, 1, _trainCar.curTrackSelection, out trainTrackSpline, out flag, preferredAltTrack);
				Vector3 targetFrontWheelTangent;
				Vector3 positionAndTangent = trainTrackSpline.GetPositionAndTangent(_trainCar.FrontWheelSplineDist, Direction, out targetFrontWheelTangent);
				_trainCar.SetTheRestFromFrontWheelData(ref trainTrackSpline, positionAndTangent, targetFrontWheelTangent);
				_trainCar.FrontTrackSection = trainTrackSpline;
				float frontWheelSplineDist;
				if (TrainTrackSpline.TryFindTrackNearby(_trainCar.GetFrontWheelPos(), 2f, out trainTrackSpline, out frontWheelSplineDist) && trainTrackSpline.HasClearTrackSpaceNear(_trainCar))
				{
					_trainCar.FrontWheelSplineDist = frontWheelSplineDist;
					Vector3 positionAndTangent2 = trainTrackSpline.GetPositionAndTangent(_trainCar.FrontWheelSplineDist, Direction, out targetFrontWheelTangent);
					_trainCar.SetTheRestFromFrontWheelData(ref trainTrackSpline, positionAndTangent2, targetFrontWheelTangent);
					_trainCar.FrontTrackSection = trainTrackSpline;
					return;
				}
			}

			public void FixedUpdate()
			{
				if (_train == null) { return; }
				Vector3 test0 = _trainCar.GetFrontWheelPos();
				Vector3 test1 = _trainCar.GetRearWheelPos();
				Vector3 test2 = plugin.RailPath[plugin.RailPath.Count - 1];
				Vector3 test3 = plugin.RailPath[0];
				test0.y = 0; test1.y = 0; test2.y = 0; test3.y = 0;
				if (Vector3.Distance(test1, test2) < 0.1f)
				{
					if (_trainCar.TrackSpeed < 0 || _trainEngine.GetThrottleFraction() < 0)
					{
						_trainCar.transform.position = plugin.RailPath[5];
						movetrain();
						_trainCar.TrackSpeed = CurrentSpeed;
						return;
					}
				}
				else if (Vector3.Distance(test0, test3) < 0.1f)
				{
					if (_trainCar.TrackSpeed >= 0 || _trainEngine.GetThrottleFraction() >= 0)
					{
						_trainCar.transform.position = plugin.RailPath[plugin.RailPath.Count - 5];
						movetrain();
						_trainCar.TrackSpeed = CurrentSpeed;
						return;
					}
				}
				else if (Vector3.Distance(test0, test2) < 0.1f)
				{
					if (_trainCar.TrackSpeed >= 0 || _trainEngine.GetThrottleFraction() >= 0)
					{
						_trainCar.transform.position = plugin.RailPath[5];
						movetrain();
						_trainCar.TrackSpeed = CurrentSpeed;
						return;
					}
				}
				else if (Vector3.Distance(test1, test3) < 0.1f)
				{
					if (_trainCar.TrackSpeed <= 0 || _trainEngine.GetThrottleFraction() <= 0)
					{
						_trainCar.transform.position = plugin.RailPath[plugin.RailPath.Count - 5];
						movetrain();
						_trainCar.TrackSpeed = CurrentSpeed;
						return;
					}
				}
				CurrentSpeed = _trainCar.TrackSpeed;
			}
		}
	}
}