// 临时探针（已完成使命，测试已移除，文件保留占位避免删除操作）
// 结论记录：① EFCore 集合投影 Select+ToList 子查询真实 SQL 翻译可用（含元素内导航路径）；
// ② FreeSql 表达式内导航访问自动 LEFT JOIN（单表与 Compose 双表均实证，R2 风险消解）。
namespace NCMapper.IntegrationTests;
