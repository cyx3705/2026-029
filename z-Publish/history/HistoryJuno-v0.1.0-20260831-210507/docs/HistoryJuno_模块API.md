# HistoryJuno 模块 API

HistoryJuno 面向 HistoryVulcan 5.1.2，指令域为 juno。模块只依赖
HistoryVulcan.Core，页面通过 HistoryAurora 1.9.2 的描述化页面协议注册。

## 页面协议

- juno.ui.describe 返回中央页面 dashboard 的 JSON 描述。
- juno.ui.actions 返回四个按钮动作的声明。
- juno.ui.data view=status 返回服务状态行，数据同时放在 CommandResult.Message 和 Data。

## 业务指令

| 指令 | 级别 | 用途 |
| --- | --- | --- |
| juno.sub2api.start | Run | 调用 sub2api-tool.ps1 start |
| juno.proxy.connect | Run | 调用 proxy-reconnect.ps1 |
| juno.sub2api.stop | Ask | 确认后调用 sub2api-tool.ps1 stop |
| juno.sub2api.status | Readonly | 探测 8080、9090、7890、17890 |

## 工具脚本解析

工具根目录优先使用环境变量 SUB2API_TOOL_ROOT，未设置时回退到桌面
easyTOOL/SU2API。脚本不存在或 PowerShell 启动失败时，指令返回失败原因，
不会在模块内创建后台进程或独立窗口。
