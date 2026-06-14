# SAFE-CSHARP-001 避免吞没异常

## 规则

不要用空的 `catch (Exception) {}` 吞掉异常。捕获具体异常类型，其余用 `throw;` 保留堆栈重新抛出。

**WRONG:**
```csharp
try
{
    return repo.Fetch(id);
}
catch (Exception)
{
    return null;
}
```

**CORRECT:**
```csharp
try
{
    return repo.Fetch(id);
}
catch (TimeoutException ex)
{
    throw new UpstreamUnavailableException("user repo timed out", ex);
}
```

原因：吞没异常会破坏可观察性并隐藏故障。
适用语言：C#
