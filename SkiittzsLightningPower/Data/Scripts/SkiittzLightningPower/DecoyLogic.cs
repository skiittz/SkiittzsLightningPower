using System;
using System.Collections.Generic;
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
using IMyEntity = VRage.ModAPI.IMyEntity;

namespace SkiittzsLightningPower
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Decoy), true)]
    public class DecoyLogic : MyGameLogicComponent
    {
	    private IMyEntity entity;
	    private static readonly MyDefinitionId ElectricityId = MyDefinitionId.Parse("GasProperties/Electricity");

	    private static bool _damageHandlerRegistered;
	    private static readonly Dictionary<long, DecoyLogic> DecoyInstances = new Dictionary<long, DecoyLogic>();

		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
	    {
			entity = Container.Entity;

			if (!_damageHandlerRegistered)
			{
				MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(1, DamageHandlerStatic);
				_damageHandlerRegistered = true;
			}

			DecoyInstances[entity.EntityId] = this;

			var resourceComponent = new MyResourceSourceComponent();
			resourceComponent.Init(MyStringHash.GetOrCompute("SolarPanels"), new MyResourceSourceInfo
			{
				ResourceTypeId = ElectricityId,
				ProductionToCapacityMultiplier = 1
			});
			resourceComponent.SetRemainingCapacityByType(ElectricityId, float.PositiveInfinity);
			resourceComponent.SetProductionEnabledByType(ElectricityId, true);
			resourceComponent.SetMaxOutputByType(ElectricityId, 0);
			resourceComponent.Enabled = true;
			entity.Components.Add(resourceComponent);

			NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
			var terminalBlock = Container.Entity as IMyTerminalBlock;
			if (terminalBlock != null)
				terminalBlock.AppendingCustomInfo += DecoyLogic_AppendingCustomInfo;
	    }

		private static void DamageHandlerStatic(object target, ref MyDamageInformation damageInfo)
		{
			if (damageInfo.Type != MyDamageType.Explosion || damageInfo.AttackerId != 0)
				return;

			var slim = target as IMySlimBlock;
			if (slim == null) return;

			var fatBlock = slim.FatBlock;
			if (fatBlock == null) return;

			DecoyLogic instance;
			if (!DecoyInstances.TryGetValue(fatBlock.EntityId, out instance))
				return;

			instance.HandleDamage(ref damageInfo);
		}

		private void HandleDamage(ref MyDamageInformation damageInfo)
		{
			if ((entity as IMyTerminalBlock)?.IsWorking != true) return;

			var incomingPower = damageInfo.Amount * 10;

			// Try to route energy to capacitors on the same grid
			var grid = (entity as IMyCubeBlock)?.CubeGrid;
			if (grid != null)
			{
				var capacitors = new List<CapacitorLogic>();
				var snapshot = new List<CapacitorLogic>(CapacitorLogic.CapacitorInstances.Values);
				foreach (var cap in snapshot)
				{
					var capGrid = cap.GetGrid();
					if (capGrid != null && capGrid.EntityId == grid.EntityId)
					{
						capacitors.Add(cap);
					}
				}

				if (capacitors.Count > 0)
				{
					// Convert the incoming power (MW) into energy (MWh) over a defined lightning event duration.
					// Here we assume a 1-second effective duration for the lightning strike.
					const double LightningEventDurationHours = 1.0 / 3600.0;
					var incomingEnergyMWh = incomingPower * LightningEventDurationHours;
					var shareEnergyMWh = incomingEnergyMWh / capacitors.Count;
					foreach (var cap in capacitors)
					{
						cap.AddLightningCharge(shareEnergyMWh);
					}

					// Successfully routed energy to capacitors; cancel explosion damage.
					damageInfo.Amount = 0;
					return;
				}
			}

			// Fallback: no capacitors on grid, use existing direct-output behavior
			var sourceComponent = entity?.Components?.Get<MyResourceSourceComponent>();
			if (sourceComponent == null) return;

			var newOutput = sourceComponent.MaxOutputByType(ElectricityId) + incomingPower;
			sourceComponent.SetMaxOutputByType(ElectricityId, newOutput);

			// Successfully increased output; cancel explosion damage.
			damageInfo.Amount = 0;
		}

		public override void UpdateAfterSimulation100()
		{
			var sourceComponent = entity.Components?.Get<MyResourceSourceComponent>();
			if (sourceComponent == null) return;

			var maxOutput = sourceComponent.MaxOutputByType(ElectricityId);
			var currentOutput = sourceComponent.CurrentOutputByType(ElectricityId);
			var excess = Math.Max(0, maxOutput - currentOutput) * 10;
			if (excess > 1)
			{
				var block = entity as IMyDecoy;
				if (block != null)
				{
					block.SlimBlock?.DoDamage((float)excess, MyStringHash.GetOrCompute("Overload"), true);
				}
			}

			sourceComponent.SetMaxOutputByType(ElectricityId, maxOutput * 0.9f);

			var terminalBlock = entity as IMyTerminalBlock;
			if (terminalBlock != null)
				terminalBlock.RefreshCustomInfo();
		}
		
		void DecoyLogic_AppendingCustomInfo(IMyTerminalBlock block, StringBuilder customInfo)
		{
			customInfo.AppendLine("Lighting Rod Active");
            var sourceComponent = entity.Components?.Get<MyResourceSourceComponent>();
			if (sourceComponent == null) return;
			customInfo.AppendLine($"Current Output: {sourceComponent.MaxOutputByType(ElectricityId).ToString("F2")}MW");
		}

		public override void Close()
		{
			var terminalBlock = entity as IMyTerminalBlock;
			if (terminalBlock != null)
				terminalBlock.AppendingCustomInfo -= DecoyLogic_AppendingCustomInfo;

			if (entity != null)
			{
				DecoyInstances.Remove(entity.EntityId);
			}

			entity = null;
			base.Close();
		}
	}
}
