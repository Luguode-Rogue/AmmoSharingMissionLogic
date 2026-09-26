using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;


namespace AmmoSharingMissionLogic
{
    public class SubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

        }

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();

        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

        }
        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            mission.AddMissionBehavior(new AmmoSharingMissionLogic());
        }
    }

    internal class AmmoSharingMissionLogic : MissionLogic
    {
        private Dictionary<int, AgentData> _agents = new Dictionary<int, AgentData>();

        public override void OnAfterMissionCreated()
        {
            base.OnAfterMissionCreated();
            _agents.Clear();
        }

        protected override void OnEndMission()
        {
            base.OnEndMission();
            _agents.Clear();
        }

        public override void OnAgentBuild(Agent agent, Banner banner)
        {
            base.OnAgentBuild(agent, banner);
            if (!agent.IsMount)
            {
                _agents.Add(agent.Index, new AgentData(agent));
            }
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);
            if (_agents.ContainsKey(affectedAgent.Index))
            {
                _agents.Remove(affectedAgent.Index);
            }
        }

        public override void OnAgentShootMissile(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, Vec3 velocity, Mat3 orientation, bool hasRigidBody, int forcedMissileIndex)
        {
            base.OnAgentShootMissile(shooterAgent, weaponIndex, position, velocity, orientation, hasRigidBody, forcedMissileIndex);
           // InformationManager.DisplayMessage(new InformationMessage($"编队剩余 {GetFormationTotalAmmo(shooterAgent)} 发"));

            // 获取当前武器的剩余弹药数量
            int currentAmmo = GetAgentCurrentAmmo(shooterAgent, weaponIndex);

            // 检查是否需要补充弹药（弹药过低且不在冷却中）
            _agents.TryGetValue(shooterAgent.Index, out var shooterData);
            if (currentAmmo <= 3 && (shooterData == null || shooterData.LastAmmoShareTime <= 0))
            {
                ShareAmmoFromTeammate(shooterAgent, weaponIndex, currentAmmo);
            }
        }

        /// <summary>
        /// 从队友那里获取弹药
        /// </summary>
        private void ShareAmmoFromTeammate(Agent shooterAgent, EquipmentIndex weaponIndex, int currentAmmo)
        {
            if (shooterAgent.Formation == null)
                return;

            // 获取射击者当前武器的类型和弹药类别
            if (weaponIndex < EquipmentIndex.WeaponItemBeginSlot || weaponIndex >= EquipmentIndex.ExtraWeaponSlot)
                return;

            var shooterWeapon = shooterAgent.Equipment[weaponIndex].CurrentUsageItem;
            if (shooterWeapon == null || !shooterWeapon.IsRangedWeapon)
                return;

            WeaponClass shooterWeaponClass = shooterWeapon.WeaponClass;
            WeaponClass shooterAmmoClass = shooterWeapon.AmmoClass;

            // 查找编队中弹药最多的队友
            Agent donorAgent = null;
            EquipmentIndex donorEquipmentIndex = EquipmentIndex.None;
            int maxAmmoAmount = 0;

            shooterAgent.Formation.ApplyActionOnEachUnit(agent =>
            {
                if (agent == null || agent.IsMainAgent || agent == shooterAgent || !agent.IsActive())
                    return;

                // 检查该队友是否刚分享过弹药
                if (_agents.TryGetValue(agent.Index, out var agentData) && agentData.LastAmmoShareTime > 0)
                    return;

                // 查找该队友身上弹药最多的相同类型武器
                for (EquipmentIndex eqIndex = EquipmentIndex.WeaponItemBeginSlot; eqIndex < EquipmentIndex.ExtraWeaponSlot; eqIndex++)
                {
                    if (agent.Equipment[eqIndex].IsEmpty)
                        continue;

                    var item = agent.Equipment[eqIndex].CurrentUsageItem;
                    if (item == null)
                        continue;

                    // 严格的武器类型匹配逻辑
                    if (!IsCompatibleWeapon(shooterWeaponClass, shooterAmmoClass, item))
                        continue;

                    int ammoAmount = agent.Equipment.GetAmmoAmount(eqIndex);
                    if (ammoAmount > maxAmmoAmount)
                    {
                        maxAmmoAmount = ammoAmount;
                        donorAgent = agent;
                        donorEquipmentIndex = eqIndex;
                    }
                }
            }, null);

            // 如果找到合适的队友，进行弹药转移
            if (donorAgent != null && maxAmmoAmount > 3)
            {
                TransferAmmo(shooterAgent, weaponIndex, currentAmmo, donorAgent, donorEquipmentIndex, maxAmmoAmount);
            }
        }

        /// <summary>
        /// 判断武器类型是否兼容（复杂的武器类型匹配逻辑）
        /// </summary>
        private bool IsCompatibleWeapon(WeaponClass shooterWeaponClass, WeaponClass shooterAmmoClass, WeaponComponentData item)
        {
            // 弩类武器 - 只能使用弩箭（Bolt）
            if (shooterWeaponClass == WeaponClass.Crossbow)
            {
                return item.WeaponClass == WeaponClass.Bolt || item.AmmoClass == WeaponClass.Bolt;
            }

            // 弓类武器 - 只能使用箭矢（Arrow）
            if (shooterWeaponClass == WeaponClass.Bow)
            {
                return item.WeaponClass == WeaponClass.Arrow || item.AmmoClass == WeaponClass.Arrow;
            }

            // 投掷类武器（标枪、飞斧等）- 必须是消耗品且远程武器
            if (shooterWeaponClass == WeaponClass.Javelin ||
                shooterWeaponClass == WeaponClass.ThrowingAxe ||
                shooterWeaponClass == WeaponClass.ThrowingKnife ||
                shooterWeaponClass == WeaponClass.Stone)
            {
                return item.IsConsumable && item.IsRangedWeapon;
            }


            // 通用匹配：武器类别或弹药类别相同
            return item.WeaponClass == shooterWeaponClass || item.AmmoClass == shooterAmmoClass;
        }

        /// <summary>
        /// 转移弹药
        /// </summary>
        private void TransferAmmo(Agent shooterAgent, EquipmentIndex shooterWeaponIndex, int shooterCurrentAmmo,
            Agent donorAgent, EquipmentIndex donorEquipmentIndex, int donorAmmoAmount)
        {
            // 计算转移数量（保留3发给捐赠者）
            int transferAmount = donorAmmoAmount - 3;
            if (transferAmount <= 0)
                return;

            // 获取射击者的目标弹药槽位（复杂的武器类型匹配）
            EquipmentIndex targetEquipmentIndex = GetMatchingAmmoSlot(shooterAgent, shooterWeaponIndex);
            if (targetEquipmentIndex == EquipmentIndex.None)
                return;

            // 计算射击者能接收的最大弹药量
            int shooterMaxAmmo = shooterAgent.Equipment[targetEquipmentIndex].MaxAmmo;
            int shooterAvailableSpace = shooterMaxAmmo - shooterCurrentAmmo;

            if (shooterAvailableSpace <= 0)
                return;

            // 实际转移数量
            int actualTransferAmount = Math.Min(transferAmount, shooterAvailableSpace);
            int newShooterAmmo = shooterCurrentAmmo + actualTransferAmount;
            int newDonorAmmo = donorAmmoAmount - actualTransferAmount;

            // 执行弹药转移
            try
            {
                shooterAgent.SetWeaponAmountInSlot(targetEquipmentIndex, (short)newShooterAmmo, false);
                donorAgent.SetWeaponAmountInSlot(donorEquipmentIndex, (short)newDonorAmmo, false);
                //InformationManager.DisplayMessage(new InformationMessage($"{shooterAgent.Name}({shooterAgent.Index}) 从 {donorAgent.Name}({donorAgent.Index}) 那里获得了 {actualTransferAmount} 发弹药，{donorAgent.Name} 剩余 {newDonorAmmo} 发，编队剩余 {GetFormationTotalAmmo(shooterAgent)} 发"));
                shooterAgent.UpdateAgentProperties();
                donorAgent.UpdateAgentProperties();

                // 设置冷却时间，避免频繁转移
                if (_agents.TryGetValue(shooterAgent.Index, out var shooterData))
                    shooterData.LastAmmoShareTime = 5.0f;
                if (_agents.TryGetValue(donorAgent.Index, out var donorData))
                    donorData.LastAmmoShareTime = 5.0f;
            }
            catch (Exception ex)
            {
                // 异常处理（可选：记录日志）
            }
        }
        private int GetFormationTotalAmmo(Agent agent) { int totalAmmo = 0; if (agent.Formation != null) { agent.Formation.ApplyActionOnEachUnit(a => { if (a != null && a.IsActive()) { for (EquipmentIndex eqIndex = EquipmentIndex.WeaponItemBeginSlot; eqIndex < EquipmentIndex.ExtraWeaponSlot; eqIndex++) { if (!a.Equipment[eqIndex].IsEmpty && a.Equipment[eqIndex].CurrentUsageItem != null && a.Equipment[eqIndex].CurrentUsageItem.IsRangedWeapon) { totalAmmo += a.Equipment.GetAmmoAmount(eqIndex); } } } }, null); } return totalAmmo; }
        /// <summary>
        /// 获取agent当前武器的弹药数量
        /// </summary>
        private int GetAgentCurrentAmmo(Agent agent, EquipmentIndex weaponIndex)
        {
            for (EquipmentIndex eqIndex = EquipmentIndex.WeaponItemBeginSlot; eqIndex < EquipmentIndex.ExtraWeaponSlot; eqIndex++)
            {
                if (!agent.Equipment[eqIndex].IsEmpty && agent.Equipment[eqIndex].CurrentUsageItem.IsRangedWeapon)
                {
                    return agent.Equipment.GetAmmoAmount(eqIndex);
                }
            }
            return 0;
        }

        /// <summary>
        /// 获取匹配的弹药槽位（复杂的武器类型判断逻辑）
        /// </summary>
        private EquipmentIndex GetMatchingAmmoSlot(Agent agent, EquipmentIndex weaponIndex)
        {
            if (weaponIndex < EquipmentIndex.WeaponItemBeginSlot || weaponIndex >= EquipmentIndex.ExtraWeaponSlot)
                return EquipmentIndex.None;

            var weaponItem = agent.Equipment[weaponIndex].CurrentUsageItem;
            if (weaponItem == null)
                return EquipmentIndex.None;

            WeaponClass weaponClass = weaponItem.WeaponClass;
            EquipmentIndex backupIndex = EquipmentIndex.None;

            // 遍历所有武器槽位，寻找匹配的弹药
            for (EquipmentIndex eqIndex = EquipmentIndex.WeaponItemBeginSlot; eqIndex < EquipmentIndex.NumAllWeaponSlots; eqIndex++)
            {
                if (agent.Equipment[eqIndex].IsEmpty)
                    continue;

                var item = agent.Equipment[eqIndex].CurrentUsageItem;
                if (item == null)
                    continue;

                // 弩类武器匹配逻辑
                if (weaponClass == WeaponClass.Crossbow)
                {
                    if (item.WeaponClass == WeaponClass.Bolt && agent.Equipment[eqIndex].Amount > 0)
                    {
                        return eqIndex; // 找到有弹药的弩箭，立即返回
                    }
                    else if (item.WeaponClass == WeaponClass.Bolt)
                    {
                        backupIndex = eqIndex; // 记录空的弩箭槽位
                    }
                }
                // 弓类武器匹配逻辑
                else if (weaponClass == WeaponClass.Bow)
                {
                    if (item.WeaponClass == WeaponClass.Arrow && agent.Equipment[eqIndex].Amount > 0)
                    {
                        return eqIndex; // 找到有弹药的箭矢，立即返回
                    }
                    else if (item.WeaponClass == WeaponClass.Arrow)
                    {
                        backupIndex = eqIndex; // 记录空的箭矢槽位
                    }
                }
                // 投掷类武器匹配逻辑
                else if (weaponItem.IsConsumable && weaponItem.IsRangedWeapon)
                {
                    if (item.IsConsumable && item.IsRangedWeapon && agent.Equipment[eqIndex].Amount > 0)
                    {
                        return eqIndex; // 找到有弹药的投掷武器，立即返回
                    }
                    else if (item.IsConsumable && item.IsRangedWeapon)
                    {
                        backupIndex = eqIndex; // 记录空的投掷武器槽位
                    }
                }
                // 通用匹配逻辑
                else if (item.WeaponClass == weaponClass || item.AmmoClass == weaponItem.AmmoClass)
                {
                    if (agent.Equipment[eqIndex].Amount > 0)
                    {
                        return eqIndex;
                    }
                    else
                    {
                        backupIndex = eqIndex;
                    }
                }
            }

            // 如果没有找到有弹药的槽位，返回备份的空槽位
            return backupIndex;
        }

        public override void OnMissionTick(float dt)
        {
            // 更新冷却时间
            foreach (var agentData in _agents.Values)
            {
                if (agentData.LastAmmoShareTime > 0)
                {
                    agentData.LastAmmoShareTime -= dt;
                    if (agentData.LastAmmoShareTime < 0)
                        agentData.LastAmmoShareTime = 0;
                }
            }
        }
    }

    /// <summary>
    /// Agent数据类，仅包含弹药分享相关数据
    /// </summary>
    public class AgentData
    {
        public readonly Agent Agent;
        public float LastAmmoShareTime { get; set; } // 上次分享弹药的时间（用于冷却）

        public AgentData(Agent agent)
        {
            Agent = agent;
            LastAmmoShareTime = 0;
        }
    }
}