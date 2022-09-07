using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
	[Info("LazyRail", "bmgjet", "0.0.3")]
	[Description("Help above ground rails function on custom maps")]
	class LazyRail : RustPlugin
	{
		public bool DisablePlayerViolationsWhenOnTrain = true;
		public bool DisablePlayerSuicideWhenOnTrain = true;
		public bool DisableDecayOnTrain = true;
		public bool TrainUnlimitedFuel = true;
		public int WagonAmount = 60;
		public int WagonsPerTrain = 10;
		public int ScanDelaySeconds = 300;
		public int SidingMinLength = 20;
		public int SidingMaxLength = 50;
		public bool ShowDebug = false;
		public List<string> wagons = new List<string>() { "assets/content/vehicles/train/trainwagonunloadable.entity.prefab", "assets/content/vehicles/train/trainwagonunloadablefuel.entity.prefab", "assets/content/vehicles/train/trainwagonunloadableloot.entity.prefab", };
		public List<TrainCar> trains = new List<TrainCar>();
		private List<Coroutine> Threads = new List<Coroutine>();
		private List<Vector3> railnodes = new List<Vector3>();
		private int Spawned = 0;

		private object OnPlayerViolation(BasePlayer player, AntiHackType type, float amount)
		{
			if (DisablePlayerViolationsWhenOnTrain && player != null)
			{
				BaseEntity be = player.GetParentEntity();
				if (be != null && be is TrainCar)
				{
					if (ShowDebug) { Puts("Prevented Player Violation"); }
					if (type == AntiHackType.InsideTerrain) return false;
					if (type == AntiHackType.NoClip) return false;
					if (type == AntiHackType.FlyHack) return false;
				}
			}
			return null;
		}

		void OnServerInitialized() { if (WagonAmount != 0) { Threads.Add(ServerMgr.Instance.StartCoroutine(TrainChecker())); } }

		private void Unload()
		{
			if (Threads != null && Threads.Count != 0)
			{
				foreach (Coroutine co in Threads) { if (co != null) ServerMgr.Instance.StopCoroutine(co); }
				Threads = null;
			}
			foreach (TrainCar t in trains)
			{
				if (!t.IsDestroyed) { t.Kill(); }
			}
		}

		private IEnumerator TrainChecker()
		{
			int checks = 0;
			var _instruction = ConVar.FPS.limit > 80 ? CoroutineEx.waitForSeconds(0.01f) : null;
			foreach (PathList pathList in World.GetPaths("Rail").AsEnumerable<PathList>())
			{
				if (pathList.Path.Points.Count() < SidingMaxLength && pathList.Path.Points.Count() > SidingMinLength)
				{
					for (int i = 10; i < pathList.Path.Points.Count() - 10; i++)
					{
						if (++checks >= 1000)
						{
							checks = 0;
							yield return _instruction;
						}
						railnodes.Add(pathList.Path.Points[i]);
					}
				}
			}
			if (railnodes.Count > 30) { Puts("Found " + railnodes.Count + " Side rails"); }
			else
			{
				Puts("Not enough Side rails found");
				yield break;
			}
			trains.Clear();
			while (true)
			{
				for (int i = 0; i < trains.Count; i++)
				{
					try
					{
						if (trains[i] == null || trains[i].IsDestroyed)
						{
							if (ShowDebug) { Puts("Removed Dead wagon reference"); }
							trains.RemoveAt(i);
						}
					}
					catch { }
				}
				if (trains.Count < WagonAmount)
				{
					foreach (BaseEntity bn in BaseEntity.serverEntities.entityList.Values)
					{
						if (bn == null) continue;
						if (bn is TrainCar) { if (!trains.Contains(bn as TrainCar)) { trains.Add(bn as TrainCar); } }
						if (++checks >= 500)
						{
							checks = 0;
							yield return _instruction;
						}
					}
				}
				for (int i2 = 0; i2 < WagonAmount - trains.Count; i2++) { SpawnEntity(railnodes.GetRandom()); }
				yield return CoroutineEx.waitForSeconds(ScanDelaySeconds);
			}
		}

		[ChatCommand("showsides")]
		private void ShowsideCmd(BasePlayer player) { if (!player.IsAdmin) { return; } foreach (Vector3 v in railnodes) { if (Vector3.Distance(v, player.transform.position) < 500) { player.SendConsoleCommand("ddraw.sphere", 8f, Color.blue, v, 1f); } } }

		[ChatCommand("showwagons")]
		private void ShowwagonCmd(BasePlayer player) { if (!player.IsAdmin) { return; } foreach (TrainCar v in trains) { if (v != null) { player.SendConsoleCommand("ddraw.sphere", 8f, Color.blue, v.transform.position, 1f); } } }

		private object OnEntityTakeDamage(BasePlayer player, HitInfo hitInfo)
		{
			if (DisablePlayerSuicideWhenOnTrain && player != null || hitInfo != null)
			{
				Rust.DamageType damageType = hitInfo.damageTypes.GetMajorityDamageType();
				if (damageType != Rust.DamageType.Suicide) return null;
				BaseEntity be = player.GetParentEntity();
				if (be != null && be is TrainCar) { if (ShowDebug) { Puts("Prevented Player Suicide"); } return false; }
			}
			return null;
		}

		private bool SpawnEntity(Vector3 position)
		{
			string prefab;
			if (Spawned >= WagonsPerTrain) { prefab = "assets/content/vehicles/locomotive/locomotive.entity.prefab"; Spawned = 0; }
			else { prefab = wagons.GetRandom(); }
			TrainCar trainCar = GameManager.server.CreateEntity(prefab, position) as TrainCar;
			trainCar.enableSaving = false;
			trainCar.platformParentTrigger.ParentNPCPlayers = true;
			trainCar.Spawn();
			trainCar.transform.position = position;
			if (trainCar == null || trainCar.IsDestroyed) { return false; }
			if (ShowDebug) { Puts("Spawned wagon @ " + trainCar.transform.position); }
			trains.Add(trainCar);
			Spawned++;
			return true;
		}

		private void OnEntitySpawned(BaseEntity baseEntity)
		{
			TrainCar _trainCar = baseEntity as TrainCar;
			if (_trainCar != null)
			{
				_trainCar.frontCollisionTrigger.interestLayers = Rust.Layers.Mask.Vehicle_World;
				_trainCar.rearCollisionTrigger.interestLayers = Rust.Layers.Mask.Vehicle_World;
				if (ShowDebug) { Puts(_trainCar.transform.position + " Edit Collisions"); }
				TrainEngine _trainEngine = _trainCar as TrainEngine;
				if (_trainEngine != null)
				{
					NextFrame(() => { if (_trainCar != null && DisableDecayOnTrain) { if (ShowDebug) { Puts(_trainCar.transform.position + " Remove Decay"); } _trainEngine.CancelInvoke("DecayTick"); } });
					if (TrainUnlimitedFuel)
					{
						_trainEngine.idleFuelPerSec = 0f;
						_trainEngine.maxFuelPerSec = 0f;
						NextFrame(() =>
						{
							StorageContainer fuelContainer = _trainEngine.GetFuelSystem()?.GetFuelContainer();
							if (fuelContainer != null)
							{
								fuelContainer.inventory.AddItem(fuelContainer.allowedItem, 1);
								fuelContainer.SetFlag(BaseEntity.Flags.Locked, true);
								if (ShowDebug) { Puts(_trainCar.transform.position + " Unlimited Fuel"); }
							}
						});
					}
				}
			}
		}
	}
}