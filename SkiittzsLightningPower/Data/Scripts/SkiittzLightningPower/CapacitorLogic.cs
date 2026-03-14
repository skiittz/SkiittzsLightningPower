using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using IMyEntity = VRage.ModAPI.IMyEntity;

namespace SkiittzsLightningPower
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false, "LightningCapacitor", "SmallLightningCapacitor")]
    public class CapacitorLogic : MyGameLogicComponent
    {
        private IMyEntity entity;
        private static readonly MyDefinitionId ElectricityId = MyDefinitionId.Parse("GasProperties/Electricity");

        internal static readonly Dictionary<long, CapacitorLogic> CapacitorInstances = new Dictionary<long, CapacitorLogic>();

        private static readonly Guid StorageGuid = new Guid("e3b6f1f9-6a2c-4a0f-9c79-0f2a3b0a5f1e");

        private float _storedEnergy;
        private const float MaxCapacity = 0.2f; // MWh
        private const float MaxDischargeRate = 10f; // MW
        private const float OverloadDamageMultiplier = 100f; // damage per MWh of excess energy
        private const float ExplosionBaseRadius = 5f; // minimum explosion radius in meters
        private const float ExplosionRadiusPerMWh = 10f; // additional radius per MWh of excess
        private const float ExplosionBaseDamage = 500f; // base explosion damage
        private const float ExplosionDamagePerMWh = 1000f; // additional damage per MWh of excess

        // Pending explosion data to avoid re-entrancy in damage handlers
        private float? _pendingExplosionExcess;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            entity = Container.Entity;

            if (entity.Storage == null)
                entity.Storage = new MyModStorageComponent();

            string storedEnergyString;
            if (entity.Storage.TryGetValue(StorageGuid, out storedEnergyString))
            {
                float parsedEnergy;
                if (float.TryParse(storedEnergyString, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedEnergy))
                {
                    _storedEnergy = parsedEnergy;
                }
            }

            CapacitorInstances[entity.EntityId] = this;

            var resourceComponent = new MyResourceSourceComponent();
            resourceComponent.Init(MyStringHash.GetOrCompute("Battery"), new MyResourceSourceInfo
            {
                ResourceTypeId = ElectricityId,
                ProductionToCapacityMultiplier = 1
            });
            resourceComponent.SetRemainingCapacityByType(ElectricityId, _storedEnergy);
            resourceComponent.SetProductionEnabledByType(ElectricityId, true);
            resourceComponent.SetMaxOutputByType(ElectricityId, 0);
            resourceComponent.Enabled = true;
            entity.Components.Add(resourceComponent);

            NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            var terminalBlock = entity as IMyTerminalBlock;
            if (terminalBlock != null)
                terminalBlock.AppendingCustomInfo += CapacitorLogic_AppendingCustomInfo;

            entity.OnClose += Entity_OnClose;
        }

        private void Entity_OnClose(IMyEntity closedEntity)
        {
            if (closedEntity == null)
                return;

            if (closedEntity.Storage != null)
            {
                closedEntity.Storage[StorageGuid] = _storedEnergy.ToString(CultureInfo.InvariantCulture);
            }

            CapacitorInstances.Remove(closedEntity.EntityId);

            var terminalBlock = closedEntity as IMyTerminalBlock;
            if (terminalBlock != null)
                terminalBlock.AppendingCustomInfo -= CapacitorLogic_AppendingCustomInfo;

            closedEntity.OnClose -= Entity_OnClose;
        }

        public void AddLightningCharge(double amount)
        {
            var headroom = (double)(MaxCapacity - _storedEnergy);
            if (amount <= headroom)
            {
                _storedEnergy += (float)amount;
                return;
            }

            // Fill to capacity, calculate excess
            _storedEnergy = MaxCapacity;
            var excess = (float)(amount - headroom);

            // Deal overload damage to the capacitor block
            var block = entity as IMyCubeBlock;
            if (block == null) return;

            var slim = block.SlimBlock;
            if (slim == null) return;

            var overloadDamage = excess * OverloadDamageMultiplier;
            slim.DoDamage(overloadDamage, MyStringHash.GetOrCompute("Overload"), true);

            // Check if the block was destroyed or made non-functional
            if (!slim.IsDestroyed && (entity as IMyTerminalBlock)?.IsFunctional == true)
                return;

            // Defer the explosion to the next frame to avoid re-entrancy in the damage system
            _pendingExplosionExcess = excess;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateAfterSimulation()
        {
            // Process any pending explosion outside the damage handler callback
            if (_pendingExplosionExcess.HasValue)
            {
                var excess = _pendingExplosionExcess.Value;
                _pendingExplosionExcess = null;

                // Remove per-frame updates since we only needed it for the deferred explosion
                NeedsUpdate &= ~MyEntityUpdateEnum.EACH_FRAME;

                TriggerOverloadExplosion(excess);
            }
        }

        private void TriggerOverloadExplosion(float excessEnergy)
        {
            if (!MyAPIGateway.Session.IsServer)
                return;

            var block = entity as IMyCubeBlock;
            if (block == null) return;

            // Clamp excess energy to avoid unbounded explosion radius/damage that could freeze the server
            const float MaxExcessEnergy = 10f; // MWh, adjust as needed for balance/performance
            if (excessEnergy > MaxExcessEnergy)
                excessEnergy = MaxExcessEnergy;

            var position = block.GetPosition();
            var radius = ExplosionBaseRadius + (excessEnergy * ExplosionRadiusPerMWh);
            var maxDamage = ExplosionBaseDamage + (excessEnergy * ExplosionDamagePerMWh);

            var sphere = new BoundingSphereD(position, radius);

            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, e => e is IMyCubeGrid);

            // Collect all damage targets first, then apply damage after iteration
            var damageTargets = new List<KeyValuePair<IMySlimBlock, float>>();

            foreach (var ent in entities)
            {
                var grid = ent as IMyCubeGrid;
                if (grid == null || grid.Closed) continue;

                if (!grid.WorldAABB.Intersects(sphere))
                    continue;

                var gridBlocks = new List<IMySlimBlock>();
                grid.GetBlocks(gridBlocks);
                foreach (var slim in gridBlocks)
                {
                    if (slim == null) continue;
                    Vector3D blockPos;
                    slim.ComputeWorldCenter(out blockPos);
                    var distance = Vector3D.Distance(position, blockPos);
                    if (distance <= radius)
                    {
                        // Damage falls off linearly with distance
                        var falloff = 1f - (float)(distance / radius);
                        var damage = maxDamage * falloff;
                        if (damage > 0)
                        {
                            damageTargets.Add(new KeyValuePair<IMySlimBlock, float>(slim, (float)damage));
                        }
                    }
                }
            }

            // Apply damage after collection is complete to avoid modifying collections during iteration
            foreach (var target in damageTargets)
            {
                try
                {
                    if (target.Key != null && !target.Key.IsDestroyed)
                    {
                        target.Key.DoDamage(target.Value, MyStringHash.GetOrCompute("Explosion"), true);
                    }
                }
                catch (Exception)
                {
                    // Block may have been destroyed by a previous damage call in this loop; skip safely
                }
            }
        }

        public IMyCubeGrid GetGrid()
        {
            var block = entity as IMyCubeBlock;
            return block?.CubeGrid;
        }

        public override void UpdateAfterSimulation100()
        {
            var sourceComponent = entity.Components?.Get<MyResourceSourceComponent>();
            if (sourceComponent == null) return;

            // Calculate how much was actually drawn since last update (~100 ticks = ~1.67 seconds)
            var currentOutput = sourceComponent.CurrentOutputByType(ElectricityId);
            var deltaTime = (100f / 60f) / 3600f; // 100 ticks / 60 ticks-per-second = ~1.667 seconds, converted to hours for MWh
            var energyUsed = currentOutput * deltaTime;
            _storedEnergy = Math.Max(0, _storedEnergy - energyUsed);

            // Set max output based on remaining stored energy
            if (_storedEnergy > 0.001f)
            {
                sourceComponent.SetMaxOutputByType(ElectricityId, MaxDischargeRate);
            }
            else
            {
                _storedEnergy = 0;
                sourceComponent.SetMaxOutputByType(ElectricityId, 0);
            }

            sourceComponent.SetRemainingCapacityByType(ElectricityId, _storedEnergy);

            var terminalBlock = entity as IMyTerminalBlock;
            if (terminalBlock != null)
                terminalBlock.RefreshCustomInfo();
        }

        private void CapacitorLogic_AppendingCustomInfo(IMyTerminalBlock block, StringBuilder customInfo)
        {
            customInfo.AppendLine("Lightning Capacitor");
            customInfo.AppendLine($"Stored Energy: {(_storedEnergy*1000).ToString("F2")} / {(MaxCapacity*1000).ToString("F2")} KWh");
            if (_storedEnergy >= MaxCapacity)
                customInfo.AppendLine("WARNING: At max capacity — overcharge will cause damage!");
            var sourceComponent = entity.Components?.Get<MyResourceSourceComponent>();
            if (sourceComponent != null)
            {
                customInfo.AppendLine($"Current Output: {sourceComponent.CurrentOutputByType(ElectricityId).ToString("F2")} MW");
                customInfo.AppendLine($"Max Discharge: {MaxDischargeRate.ToString("F2")} MW");
            }
        }

        public override void Close()
        {
            var terminalBlock = entity as IMyTerminalBlock;
            if (terminalBlock != null)
                terminalBlock.AppendingCustomInfo -= CapacitorLogic_AppendingCustomInfo;

            if (entity != null)
            {
                CapacitorInstances.Remove(entity.EntityId);
            }

            _pendingExplosionExcess = null;
            entity = null;
            base.Close();
        }
    }
}
