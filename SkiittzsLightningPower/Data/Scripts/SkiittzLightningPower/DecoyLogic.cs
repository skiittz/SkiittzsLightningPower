using System;
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
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Decoy), false)]
    public class DecoyLogic : MyGameLogicComponent
    {
	    private IMyEntity entity;
	    private double lastStrike;
	    public MyDefinitionId ElectricityId => MyDefinitionId.Parse("GasProperties/Electricity");
		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
	    {
			MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(1, DamageHandler);
			entity = Container.Entity;
			var resourceComponent = new MyResourceSourceComponent();
			resourceComponent.Init(MyStringHash.GetOrCompute("SolarPanels"), new MyResourceSourceInfo
			{
				ResourceTypeId = ElectricityId,
				ProductionToCapacityMultiplier = 1
			});
			resourceComponent.SetRemainingCapacityByType(ElectricityId, float.PositiveInfinity);
			resourceComponent.SetProductionEnabledByType(ElectricityId,true);
			resourceComponent.SetMaxOutputByType(ElectricityId, 20);
			resourceComponent.Enabled = true;
			entity.Components.Add(resourceComponent);

			NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
			var terminalBlock = Container.Entity as IMyTerminalBlock;
			terminalBlock.AppendingCustomInfo += DecoyLogic_AppendingCustomInfo;
	    }

		private void DamageHandler(object target, ref MyDamageInformation damageInfo)
		{
			if (damageInfo.Type == MyDamageType.Explosion && damageInfo.AttackerId == 0)
		    {
			    if ((entity as IMyTerminalBlock)?.IsWorking != true) return;

				var sourceComponent = entity?.Components?.Get<MyResourceSourceComponent>();
				if(sourceComponent == null) return;

				var incomingPower = damageInfo.Amount / 20;
				var newOutput = sourceComponent.MaxOutput + incomingPower;
				sourceComponent.SetMaxOutput(newOutput);
				damageInfo.Amount = 0;
		    }
		}

		public override void UpdateAfterSimulation100()
		{
			var sourceComponent = entity.Components?.Get<MyResourceSourceComponent>();
			if (sourceComponent == null) return;

			var excess = Math.Max(0, sourceComponent.MaxOutput - sourceComponent.CurrentOutput) * 10;
			if (excess > 1)
			{
				var block = (entity as IMyDecoy);
				if (block != null)
				{
					block.SlimBlock?.DoDamage(excess, MyStringHash.GetOrCompute("Overload"), true);
				}
			}

			sourceComponent.SetMaxOutput(sourceComponent.MaxOutput*0.9f);
		}
		
		void DecoyLogic_AppendingCustomInfo(IMyTerminalBlock block, StringBuilder customInfo)
		{
			var sourceComponent = entity.Components?.Get<MyResourceSourceComponent>();
			if (sourceComponent == null) return;
			customInfo.Append($"Current Output: {sourceComponent.MaxOutput.ToString("F2")}MW\n");
		}
	}
}
