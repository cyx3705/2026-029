# HistoryJuno

> Sub2API 账号与服务控制面板：启动、代理重连、账号管理与导入

![OneHistory Logo](./Logo.png)

## 定位

HistoryJuno 是加载到 HistoryVulcan、显示在 HistoryAurora 中的 Sub2API 控制面板。`Juno` 页面提供启动、代理重连、
关闭和刷新，并在「账号管理 / 服务状态 / 账号导入」三个子页间切换。

- 复用现有 Sub2API PowerShell 工具，不重写 Sub2API 服务本体或代理内核。
- 不创建独立网页或自建控件；账号导入子页复用 Aurora 通用 JSON 来源组件。

## 概况

| 项 | 值 |
| --- | --- |
| 编号 | `2026-029` |
| 角色 | 宿主模块（`kind=module`） |
| 指令域 | `juno` |
| 界面 | Aurora 描述化页面 `Juno` |
| MCP 投影 | `readonly`；`juno.ui.*` 与 `juno.import.select` 对远程模型隐藏 |
| 版本与宿主下限 | [`HistoryJunoVersion.props`](./b-Code/HistoryJuno/HistoryJunoVersion.props)；宿主下限见 [模块 API](./b-Office/package/模块API.md) |

## 能力

| 指令 | 用途 |
| --- | --- |
| `juno.sub2api.start` | 分阶段启动 Ubuntu Docker 与 Sub2API compose，8080/9090 就绪才算成功 |
| `juno.sub2api.stop` | 确认后停止服务 |
| `juno.sub2api.status` | 探测 8080、9090、7890、17890 端口 |
| `juno.proxy.connect` | 更新端口转发、Sub2API 全局代理与代理 IP |
| `juno.accounts.import` | 导入账号并绑定所选分组与唯一全局代理 |

页面协议与导入流程见 [模块 API](./b-Office/package/模块API.md)。

## 入口

| 入口 | 用途 |
| --- | --- |
| [`AGENTS.md`](./AGENTS.md) | AI 工作合同：读取顺序、真值判定、边界 |
| [`project.manifest.json`](./project.manifest.json) | 项目身份、活动目录、文档与命令 |
| [文档中心](./b-Office/文档中心.md) | 文档索引与读取顺序 |
| [项目概览](./b-Office/current/项目概览.md) | 目标、范围与状态 |
| [技术合同](./b-Office/current/技术合同.md) | 现行需求与架构 |
| [有效决策](./b-Office/current/有效决策.md) | 仍然有效的关键决策 |
| [验证合同](./b-Office/current/验证合同.md) | 验证层级、命令与证据 |
| [模块 API](./b-Office/package/模块API.md) | 跨模块消费合同 |

## 目录

| 路径 | 职责 |
| --- | --- |
| `b-Code/HistoryJuno/` | 模块源码与 manifest |
| `b-Code/HistoryJuno.Tests/` | 自动验证 |
| `b-Code/` | 项目合同检查 |
| `b-Office/` | 项目文档：`current/` 现行合同、`package/` 消费合同、`history/` 只读归档 |
| `z-Publish/` | 正式快照，由宿主管线写入 |

## 构建与验证

构建命令以 [`project.manifest.json`](./project.manifest.json) 的 `commands.build` 为准：它把中间文件与输出重定向到
`C:\Users\Administrator\Desktop\easyTOOL\SU2API\.build`（F 盘工作树的默认 `obj` 受文件系统策略限制）。

```powershell
dotnet run --project .\b-Code\HistoryJuno.Tests\HistoryJuno.Tests.csproj -c Release -p:NuGetAudit=false
powershell -NoProfile -ExecutionPolicy Bypass -File .\b-Code\Test-ProjectContract.ps1 -Instantiation
```

## 开发与发布

改动只进 `vulcan.dev.start` 创建的工作区，经宿主 Console CLI 走
`vulcan.dev.start` → `vulcan.dev.submit`（候选构建并热装送审）→ `vulcan.dev.finish`（批准后并回并写入 `z-Publish`）。
本仓不自行发布。

## 要点

- 停止与代理动作依赖 `sub2api-tool.ps1` 与 `proxy-reconnect.ps1`，从 `SUB2API_TOOL_ROOT` 或桌面 `easyTOOL/SU2API` 查找。
- 账号、分组、代理和导入走本机 Sub2API 管理接口；凭据目录由 `SUB2API_DEPLOY_ROOT` 指定，默认 `%USERPROFILE%/Projects/sub2api-deploy`。
- 账号 JSON、管理密码、令牌和原始响应只在内存及 127.0.0.1 回环中使用，不进日志或页面数据。

---

作者：Pinavia
