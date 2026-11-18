<!-- 来源：NCMapper 主技术文档 §22 常见陷阱与诊断 -->
## 22. 常见陷阱与诊断

| 陷阱                                  | 现象                                                                                        | 正确处理                                                                        |
| ----------------------------------- | ----------------------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| `[MapProperty]` 带点路径 `xx.xx`        | **四引擎均支持**；失败为显性可观测：源生成器 NCMAP001/005 Warning（带失败段名）且不生成映射；运行时抛 MapConfigurationException（显性失败，不静默跳过）；投影跳过 + Trace                        | 点路径按段解析（段匹配 OrdinalIgnoreCase，中间节点须 class/interface 非值类型/string）；`[Mask]`/`[Convert]` × 点路径暂不消费（可观测诊断 NCMAP030/警告，暂未消费）；跨表导航仍可用 `[MapProperty(typeof(NavType), "Field")]` 显式指定导航实体类型 |
| 数组 `T[].Contains(T)` 在表达式树 lambda 中 | .NET 10 / C# 14 下绑定 `MemoryExtensions.Contains`，FreeSql 无法翻译 → 运行期抛 NotSupportedException | 改用 `idArray.AsEnumerable().Contains(...)`；源生成器会发出 NCMAP018 警告提醒             |
| FreeSql 多表只替换单表数据源                  | `WithSql(sqlT1, sqlT2, …)` 的 sqlT2 必填，无法只替换 T1                                            | 多表 WithSql 须同时提供所有表数据源                                                      |
| 命名约定扁平化 ≠ 点路径                       | `AddressCity → src.Address.City` 是自动拆解目标属性名大写边界，与手写 `.` 点路径无关                             | 点路径 `"Address.City"` 需显式 `[MapProperty(typeof(T), "Address.City")]` 声明；推导扁平化自动触发 |

