# HistoryJuno 模块 API

HistoryJuno 面向 HistoryVulcan 5.1.2，指令域为 juno。模块只依赖
HistoryVulcan.Core，页面通过 HistoryAurora 1.9.2 的描述化页面协议注册。

## 页面协议

- juno.ui.describe 返回中央页面 sub2api 的 JSON 描述；控制面板第二行通过
  juno.section 通道在“账号状态”和“服务状态”两个 switch case 间切换，默认账号状态。
- juno.ui.actions 返回四个按钮动作的声明。
- juno.ui.data view=accounts 返回 OpenAI OAuth 账号状态以及 5h/7d 已用额度；
  行字段固定为 account、status、fiveHour、sevenDay。
- juno.ui.data view=status 返回服务状态行，数据同时放在 CommandResult.Message 和 Data。
- 不传 view 时仍按兼容行为返回 status；未知 view 返回失败。

## 业务指令

| 指令 | 级别 | 用途 |
| --- | --- | --- |
| juno.sub2api.start | Run | 单实例调用 sub2api-tool.ps1 start；代理探测和脚本有超时上限，仅在 8080/9090 就绪后返回成功 |
| juno.proxy.connect | Run | 调用 proxy-reconnect.ps1 |
| juno.sub2api.stop | Ask | 确认后调用 sub2api-tool.ps1 stop |
| juno.sub2api.status | Readonly | 探测 8080、9090、7890、17890 |

## 工具脚本解析

工具根目录优先使用环境变量 SUB2API_TOOL_ROOT，未设置时回退到桌面
easyTOOL/SU2API。脚本不存在或 PowerShell 启动失败时，指令返回失败原因，
不会在模块内创建后台进程或独立窗口。

## 账号状态读取

管理接口固定为本机 http://127.0.0.1:8080。部署目录优先使用环境变量
SUB2API_DEPLOY_ROOT，未设置时回退到 %USERPROFILE%/Projects/sub2api-deploy；模块只在
内存中读取 CREDENTIALS.txt 的 ADMIN_EMAIL 与 ADMIN_PASSWORD，登录后分页查询 OpenAI OAuth
账号并批量读取额度。密码、令牌、原始响应和账号凭据不会进入日志、命令结果或页面数据。

状态列依次识别限流、过载、错误、临时不可调度、停用、暂停调度和正常。额度显示为
“已用 34% · 2h18m 后重置”；无窗口为“-”，单账号额度读取失败为“获取失败”。
