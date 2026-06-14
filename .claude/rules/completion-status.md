# Completion Status Protocol

每次任务结束时，使用以下状态之一标记结果：

- `DONE`: 所有验收标准通过，验证命令成功。
- `DONE_WITH_CONCERNS`: 核心功能完成，但存在已知的非阻塞问题。
- `BLOCKED`: 存在外部依赖或技术阻碍，无法继续。
- `NEEDS_CONTEXT`: 需要更多信息才能继续。
- 没有通过验证命令就不算 DONE。
