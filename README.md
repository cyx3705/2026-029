# HistoryJuno

HistoryJuno 是一个加载到 HistoryVulcan 并显示在 HistoryAurora 主页面中的
Sub2API 轻量控制面板。它复用本机 PowerShell 工具，提供启动服务、连接代理、
关闭服务和刷新状态四个入口，不创建独立网页。

## 项目入口

| 入口 | 用途 |
| --- | --- |
| AGENTS.md | 修改边界与验证规则 |
| project.manifest.json | 项目身份、活动目录和构建命令 |
| b-Code/HistoryJuno | 模块源码与 manifest |
| b-Office/current | 当前需求、决策和验证合同 |
| b-Office/package/HistoryJuno_模块API.md | 模块命令与页面协议 |

## 构建

在 F 盘工作树中运行 project.manifest.json 的 build 命令。当前工作树的默认
obj 目录受文件系统策略限制，构建命令已将中间文件和输出放到 C 盘 .build。

模块依赖 HistoryVulcan 5.1.2 发布快照，并从 SUB2API_TOOL_ROOT 或桌面
easyTOOL/SU2API 查找 sub2api-tool.ps1 与 proxy-reconnect.ps1。
