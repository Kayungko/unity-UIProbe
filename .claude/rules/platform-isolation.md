# ARCH-PLATFORM-001 把平台代码隔离在桥接层后面

## 规则

业务逻辑不得直接调用操作系统、窗口或原生文件对话框等 API；封装在平台适配层后面。

```text
错误做法：
core/export -> 直接调用原生文件对话框 / 注册表 API

正确做法：
core/export -> PlatformBridge.pickFile() / Settings 接口
```

原因：原生 API 绑定特定操作系统；隔离后业务逻辑可测试、可跨平台。
适用范围：所有桌面应用的业务逻辑层。
