# HistoryJuno 模块 API

HistoryJuno 0.4.0 面向 HistoryVulcan 5.1.2，指令域为 `juno`。模块只依赖
HistoryVulcan.Core，页面通过 HistoryAurora 1.15.0 及以上的描述化页面协议注册。
来源选择器通过 `commitAction` 回写路径；程序回写不再次提交，且不执行账号导入。

## 页面协议

- `juno.ui.describe` 只返回标题为 `Juno` 的 `sub2api` 主页面；账号导入是其内部子页。
- Juno 主页面通过 `juno.section` 在“账号管理”、“服务状态”和“账号导入”间切换。账号管理上方有两个
  `textbox mode=select` 多态框，分别发布 `juno.account.group` 与 `juno.account.status` 通道。
- `juno.ui.groups purpose=filter|import` 返回动态分组候选，每行只有 `value` 字段；filter 模式
  额外返回“全部分组”。
- `juno.ui.data view=accounts [group=...] [status=...]` 返回 OpenAI OAuth 账号行，字段为
  `account`、`group`、`status`、`proxy`、`fiveHour`、`sevenDay`。
- `juno.ui.data view=status` 返回服务状态行。不传 view 时仍按兼容行为返回 status；未知 view 失败。
- 账号导入页使用 Aurora `sourcePicker`，选择命令为 `juno.import.select`；导入分组由
  `juno.ui.groups purpose=import` 提供。三个子页共用顶部服务控制；刷新动作（含兼容的 juno.import.refresh）均指向 sub2api。

## 业务指令

| 指令 | 级别 | 用途 |
| --- | --- | --- |
| `juno.sub2api.start` | Run | 单实例分阶段启动 Ubuntu Docker 与 Sub2API compose；阶段有超时上限，仅在 8080/9090 就绪后返回成功 |
| `juno.proxy.connect` | Run | 调用 `proxy-reconnect.ps1`，更新端口转发、Sub2API 全局代理和代理 IP 探测结果 |
| `juno.sub2api.stop` | Ask | 确认后调用 `sub2api-tool.ps1 stop` |
| `juno.sub2api.status` | Readonly | 探测 8080、9090、7890、17890 |
| `juno.accounts.import source=<json> group=<候选>` | Ask | 导入账号，并给新账号绑定所选分组和唯一全局代理 |

`juno.import.select`、`juno.ui.groups` 和其他 `juno.ui.*` 是页面内部协议，对远程模型隐藏。

## 账号导入

来源必须是本机 `.json` 文件，大小不超过 16 MB。根对象可使用 Sub2API 标准 `data.accounts`
形状，也可直接包含 `accounts`/`proxies`，Juno 会补齐 data 包装与 `exported_at`。

导入发生前，Juno 登录本机管理端，确认所选分组仍为 active，并读取活动代理。代理按
`ip_address` 去重后必须恰好只有一个不同值；没有已探测 IP 时先执行“代理重连”，存在多个 IP 时
先收敛代理配置。Juno 不依赖固定代理 ID 或名字，而是从唯一 IP 对应代理中选择最小 ID。

导入成功后，Juno 比较导入前后的账号 ID，逐个读取新增账号详情，再以原 credentials、所选
`group_ids` 和全局 `proxy_id` 写回。JSON、credentials、管理密码、访问令牌、原始响应和错误响应体
不会进入日志、确认文本、命令结果或页面数据。导入同一时刻只允许一个任务，请求上限为 120 秒。

## 工具脚本解析

工具根目录优先使用环境变量 `SUB2API_TOOL_ROOT`，未设置时回退到桌面
`easyTOOL/SU2API`。脚本不存在或 PowerShell 启动失败时，指令返回固定错误原因，不在模块内创建
常驻后台进程或独立页面。

## 账号状态读取

管理接口固定为 `http://127.0.0.1:8080`。部署目录优先使用环境变量 `SUB2API_DEPLOY_ROOT`，
未设置时回退到 `%USERPROFILE%/Projects/sub2api-deploy`；模块只在内存中读取 `CREDENTIALS.txt`
的 `ADMIN_EMAIL` 与 `ADMIN_PASSWORD`。

状态列依次识别限流、过载、错误、临时不可调度、停用、暂停调度和正常。额度显示为
“已用 34% · 2h18m 后重置”；无窗口为“-”，单账号额度读取失败为“获取失败”。
