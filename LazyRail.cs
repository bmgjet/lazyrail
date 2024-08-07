namespace Oxide.Plugins
{
    [Info("LazyRail", "bmgjet", "0.0.5")]
    [Description("Help above ground rails function on custom maps")]
    class LazyRail : RustPlugin
    {
        public bool DisablePlayerViolationsWhenOnTrain = true;
        public bool DisablePlayerSuicideWhenOnTrain = true;
        public bool DisableDecayOnTrain = true;
        public bool TrainUnlimitedFuel = true;
        public bool ShowDebug = false;

        private object OnPlayerViolation(BasePlayer player, AntiHackType type, float amount)
        {
            if (DisablePlayerViolationsWhenOnTrain && player != null)
            {
                BaseEntity be = player.GetParentEntity();
                if (be != null && be is TrainCar)
                {
                    if (ShowDebug) { Puts("Prevented Player Violation"); }
                    if (type == AntiHackType.InsideTerrain) return true;
                    if (type == AntiHackType.NoClip) return true;
                    if (type == AntiHackType.FlyHack) return true;
                }
            }
            return null;
        }

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

        private void OnEntitySpawned(TrainCar _trainCar)
        {
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
                            StorageContainer fuelContainer = BaseNetworkable.serverEntities.Find(_trainEngine.GetFuelSystem().GetInstanceID()) as StorageContainer;
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