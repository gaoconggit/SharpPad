为指定功能需求创建新的 git worktree 开发分支

功能需求描述: `$ARGUMENTS`

我将为您创建一个新的 worktree 来开发这个功能：

1. 检查当前 git 状态和确保 worktrees 目录存在
2. 根据功能描述生成规范化的分支名和目录名
3. 创建新的 worktree 分支
4. 显示创建结果

让我开始执行：

首先检查 git 状态：
```bash
git status
```

确保 worktrees 目录存在：
```bash
mkdir -p worktrees
```

根据功能描述 "$ARGUMENTS" 生成分支名和目录名：
- 将中文转换为英文关键词
- 转换为小写并用连字符连接
- 添加 feature/ 前缀

创建 worktree：
```bash
# 生成的分支名和目录名将基于功能描述
git worktree add worktrees/[normalized-name] -b feature/[normalized-name]
```

**示例用法:**
- `/create-worktree Roslyn 气泡提示 Provider`
- `/create-worktree 添加用户权限管理`
- `/create-worktree 优化代码编译性能`