# Decision Protocol

1. 先从仓库、task 文件和文档中发现事实，再决定是否需要提问。
2. 如果仍然需要决策，一次只处理一个决策。
3. 在给出选项之前，先重新落地当前 task、milestone 和受影响模块。
4. 用直白语言解释选项，明确推荐方案，并说明取舍。
5. 优先选择满足需求的最小完整改动。

### Structured AskUserQuestion Format

使用 `AskUserQuestion` 收集决策时，遵循下面 5 步：

1. **重新落地上下文**: 先用一句话重述当前 task、milestone，以及为什么需要这个决策。
2. **降低理解门槛**: 把技术选项翻译成业务或交付影响。
3. **给出推荐**: 给推荐选项标注 `(Recommended)`。
4. **提供选项**: 给出 2-4 个互斥选项。
5. **一次只问一个决策**: 不要把多个无关决策塞进同一个 `AskUserQuestion`。

### Completeness Reference

AI 辅助下，完整实现的成本差距通常很小，优先完整方案。

| Task Type | Human Effort | CC Effort | Shortcut Saves | Complete Saves |
|-----------|-------------|-----------|---------------|---------------|
| New API endpoint + tests | 4 h | 15 min | 10 min | 15 min |
| Unit test backfill | 2 h | 8 min | 5 min | 8 min |
| Bug fix + regression test | 3 h | 12 min | 8 min | 12 min |
| Refactor + update docs | 3 h | 10 min | 6 min | 10 min |

需要避免的反模式：
- 因为改动小就跳过测试
- 把错误处理延后
- 只实现 happy path
