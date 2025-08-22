用合理描述性信息提交所有变更文件
<doc>
type:代表某次提交的类型，比如是修复一个bug还是增加一个新的feature。所有的type类型如下：
feature：新增feature
fix: 修复bug
docs: 仅仅修改了文档，比如README, CHANGELOG, CONTRIBUTE等等
style: 仅仅修改了空格、格式缩进、逗号等等，不改变代码逻辑
refactor: 代码重构，没有加新功能或者修复bug
perf: 优化相关，比如提升性能、体验test: 测试用例，包括单元测试、集成测试等
chore: 改变构建流程、或者增加依赖库、工具等
revert: 回滚到上一个版本

示例:
fix # 修复新增用户报错问题
1.完善字段验证
2.修改返回值

feature # 权限服务-添加数据权限
</doc>

