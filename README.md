# 矿脉保护
一个用于《戴森球计划》的矿脉保护Mod。当矿脉的单个矿点剩余1个矿物时停止采矿，避免矿脉枯竭影响存档上限。当前原版游戏矿物利用390级后矿物会变为0消耗，此时Mod不再进行保护，正常挖矿。

## 功能
- 普通有限固体矿点剩余**1**个矿物时进入保护状态。
- 受保护矿点不再消耗矿物，也不再产出矿物。
- 小型采矿机和大型采矿机的速率详情会排除已保护矿点的贡献。
- 游戏内设置选项提供Mod开关。
- 可在生产统计面板把鼠标移至参考速率详情，查看已保护矿点数量和保护状态参考速率。

## 设置
进入 **设置 → 游戏 → 矿脉保护** 即可启用或禁用，默认开启。切换后立即生效，不需要重启游戏或重新载入存档。

![中文设置界面示意图](https://raw.githubusercontent.com/SkyeDrem/VeinProtector/main/image/%E8%AE%BE%E7%BD%AE%E4%B8%AD%E6%96%87.png)

## 矿物利用等级与零消耗行为
Mod不直接判断科技等级，而是读取游戏当前实际的矿物资源消耗率。
当实际资源消耗率降为0时，剩余矿物数量为1的矿点不再需要保护，被保护矿点会重新被开采，在当前原版机制下对应矿物利用390级。

## 参考速率
生产统计面板外部显示的“参考速率”保持原版计算，Mod不修改该数值。
在支持的固体矿物参考速率详情中(当鼠标移动到参考速率数值上时)，Mod额外显示：
- **已保护矿点：N**————当前统计范围内，实际绑定到矿机且被保护的矿点数量。
- **保护状态参考速率：X / min**————从原版理论参考速率中扣除受保护矿点对应的矿机贡献后，当前工厂布局的理论生产能力。
保护状态参考速率是排除受保护矿点产能后的区域理论最高产值，该信息可帮助玩家在资源消耗率降为零之前规划生产。
当保护开关关闭或资源消耗率为零时，详情扩展会隐藏，恢复原版布局。

![中文参考速率详情面板示意图](https://raw.githubusercontent.com/SkyeDrem/VeinProtector/main/image/%E5%8F%82%E8%80%83%E9%9D%A2%E6%9D%BF%E4%B8%AD%E6%96%87.png)

## 适用范围
Mod只处理普通有限固体矿脉。原油等其它非有限固体矿脉的生产逻辑保持原版。

## 保存与卸载
Mod不向存档写入额外的专用状态。被保护的矿点仍是游戏原生矿脉数据。关闭保护功能或卸载Mod后，这些矿点可以继续按照原版逻辑被采尽。

## 兼容性
UXAssist含有矿脉保护功能，若已安装UXAssist Mod则不再需要本Mod。

## 安装
推荐使用r2modman / Thunderstore Mod Manager，将BepInEx和VeinProtector安装到同一个文件夹，并通过Mod管理器启动游戏。
手动安装时，将 `VeinProtector.dll` 放入：BepInEx/plugins/VeinProtector/VeinProtector.dll
标准发布ZIP已包含 `BepInEx/plugins/VeinProtector/` 目录结构，可按该结构解压。不要在同一Profile中同时安装管理器副本和另一个手动DLL副本。

## 开发 / Building
从源码构建需要本机安装的《戴森球计划》游戏程序集，以及 BepInEx/Harmony 引用。由于版权原因，这些 DLL 不包含在仓库中。将它们放入 `src/VeinProtector/AssemblyFromGame/` 和 `src/VeinProtector/AssemblyFromProfile/`（这两个目录已被 Git 忽略），然后运行 `scripts/build.ps1`。

## 问题反馈
反馈问题可至**bilibili@SkyeDrem**。

## 致谢
感谢《戴森球计划》制作组带来并持续维护这款优秀的游戏。
感谢UXAssist项目及其公开实现，为本Mod在矿脉保护机制和游戏行为分析方面提供了重要参考。
感谢BepInEx、Harmony/HarmonyX以及《戴森球计划》Mod社区提供的工具、资料与技术积累。
感谢ChatGPT，本Mod由AI协助制作。


# VeinProtector

A vein protection mod for *Dyson Sphere Program*. When an individual vein node has only 1 resource remaining, mining of that node stops to prevent vein depletion from affecting save limits. In the current vanilla game, resource consumption reaches zero at Veins Utilization level 390. At that point, the mod no longer applies protection and mining proceeds normally.

## Features

- Ordinary finite solid vein nodes enter protection when only **1** resource remains.
- Protected vein nodes no longer consume resources and no longer produce resources.
- Mining rate details for both regular miners and advanced miners exclude the contribution of protected vein nodes.
- An in-game option is provided to enable or disable the mod.
- In the Production Statistics panel, hover over the Reference Rate value to view the number of protected vein nodes and the Protected-State Reference Rate.

## Configuration

Go to **Settings → Game → Vein Protection** to enable or disable the mod. It is enabled by default. Changes take effect immediately and do not require restarting the game or reloading the save.

![English settings interface](https://raw.githubusercontent.com/SkyeDrem/VeinProtector/main/image/%E8%AE%BE%E7%BD%AE%E8%8B%B1%E6%96%87.png)

## Veins Utilization Level and Zero-Consumption Behavior

The mod does not directly check the technology level. Instead, it reads the game's current actual resource-consumption rate.

When the actual resource-consumption rate reaches 0, vein nodes with only 1 resource remaining no longer require protection, and previously protected nodes will resume mining. Under the current vanilla game mechanics, this corresponds to Veins Utilization level 390.

## Reference Rate

The **Reference Rate** shown in the Production Statistics panel remains calculated by the vanilla game. The mod does not modify this value.

For supported solid resources, when hovering over the Reference Rate value to open its details, the mod additionally displays:

- **Protected Vein Nodes: N** — The number of protected vein nodes within the current statistics scope that are actually connected to miners.
- **Protected-State Reference Rate: X / min** — The theoretical production capacity of the current factory layout after subtracting the miner contribution of protected vein nodes from the vanilla theoretical Reference Rate.

The Protected-State Reference Rate represents the theoretical maximum production capacity of the selected area after excluding the capacity of protected vein nodes. This information can help players plan production before the resource-consumption rate reaches zero.

When vein protection is disabled or the resource-consumption rate reaches zero, the additional details are hidden and the vanilla layout is restored.

![English Reference Rate details](https://raw.githubusercontent.com/SkyeDrem/VeinProtector/main/image/%E5%8F%82%E8%80%83%E9%9D%A2%E6%9D%BF%E8%8B%B1%E6%96%87.png)

## Scope

The mod only affects ordinary finite solid veins. Oil and other non-finite solid resource production systems remain unchanged from vanilla.

## Save Compatibility and Uninstalling

The mod does not write any additional mod-specific state into save files. Protected vein nodes remain standard vanilla vein data. If vein protection is disabled or the mod is uninstalled, these nodes can continue to be depleted normally according to vanilla game behavior.

## Compatibility

UXAssist includes its own vein protection feature. If you already use the UXAssist mod, you generally do not need this mod as well.

## Installation

Using **r2modman / Thunderstore Mod Manager** is recommended. Install both BepInEx and VeinProtector into the same profile, then launch the game through the mod manager.

For manual installation, place `VeinProtector.dll` at:

`BepInEx/plugins/VeinProtector/VeinProtector.dll`

The standard release ZIP already contains the `BepInEx/plugins/VeinProtector/` directory structure and can be extracted accordingly. Do not install both a mod-manager copy and a separate manually installed DLL in the same profile.

## Building
Building from source requires game assemblies from a local Dyson Sphere Program installation plus BepInEx/Harmony references. These DLLs are omitted from the repository for copyright reasons. Provide them under `src/VeinProtector/AssemblyFromGame/` and `src/VeinProtector/AssemblyFromProfile/` (both directories are Git-ignored), then run `scripts/build.ps1`.

## Bug Reports

For bug reports, contact **bilibili @SkyeDrem**.

## Acknowledgements

Thanks to the developers of *Dyson Sphere Program* for creating and continuing to maintain this excellent game.

Thanks to the UXAssist project and its publicly available implementation, which provided valuable reference material for vein protection mechanics and analysis of game behavior.

Thanks to BepInEx, Harmony/HarmonyX, and the *Dyson Sphere Program* modding community for the tools, documentation, and accumulated technical knowledge that made this project possible.

Thanks to ChatGPT. This mod was developed with AI assistance.
