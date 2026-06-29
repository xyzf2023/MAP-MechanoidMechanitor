# ScenarioProtagonist → MechanicalConsciousnessHost 旧存档兼容

该文件夹只用于将旧存档键 `mechanoidMechanitorScenarioProtagonist` 合并到 `mechanicalConsciousnessHost`。

## 删除方式

正式发布前，如不再需要支持当前开发阶段旧存档，可以删除整个：

`Source/LegacySaveCompatibility/ScenarioProtagonistToMechanicalConsciousnessHost/`

删除后无需修改 `.csproj`（项目使用默认的 `EnableDefaultCompileItems` 自动包含 `Source` 下的 `.cs` 文件）。

## 删除后的影响

- 由于使用了无实现也合法的 `partial void ExposeLegacyScenarioProtagonistMigration()`，删除文件夹后正式代码仍可编译。
- 删除兼容文件夹后，尚未迁移过的旧存档将无法再从旧剧本主角字段恢复机械意识宿主。
