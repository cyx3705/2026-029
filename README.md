# HistoryJuno

> Sub2API 账号与服务控制面板：启动、代理重连、账号管理与导入

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
| 版本与宿主下限 | [`HistoryJunoVersion.props`](./b-Code/HistoryJuno/HistoryJunoVersion.props)；最低宿主 HistoryVulcan 6.0.0 |

## 能力

| 指令 | 用途 |
| --- | --- |
| `juno.sub2api.start` | 分阶段启动 Ubuntu Docker 与 Sub2API compose，8080/9090 就绪才算成功 |
| `juno.sub2api.stop` | 确认后停止服务 |
| `juno.sub2api.status` | 探测 8080、9090、7890、17890 端口 |
| `juno.proxy.connect` | 更新端口转发、Sub2API 全局代理与代理 IP |
| `juno.accounts.import` | 导入账号并绑定所选分组与唯一全局代理 |

指令参数读注册自描述：`diana.docs.read domain=juno`（宿主 6.1.0 起没有消费文档）。取舍与外部路径见 [现行约定](./b-Office/现行约定.md)。

## 入口

| 入口 | 用途 |
| --- | --- |
| [`AGENTS.md`](./AGENTS.md) | AI 工作合同：读取顺序、真值判定、边界 |
| [`project.manifest.json`](./project.manifest.json) | 项目身份、活动目录、文档与命令 |
| [现行约定](./b-Office/现行约定.md) | 唯一的长期文档：真相在哪、取舍、真机才知道的、工程约定 |

## 目录

| 路径 | 职责 |
| --- | --- |
| `b-Code/HistoryJuno/` | 模块源码与 manifest |
| `b-Code/HistoryJuno.Tests/` | 自动验证 |
| `b-Code/` | 项目合同检查 |
| `b-Office/` | 唯一长期文档 `current/现行约定.md` |
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

## 保留内容

### logo与作者

- 作者：pinavia
- ![logo](./Logo.png)

### 学习许可与权利保留

- 本项目允许个人、学校和研究者进行非商业性的学习、研究和引用。
- 未经著作权人书面许可，不得将项目内容用于商业开发、产品制造、商业服务、商业宣传、投标交付、毕业设计成品交付或再授权。
- 不得删除作者、来源、版本历史和第三方许可证声明，不得将本项目或其衍生成果宣称为与作者无关的原创项目。
- 软件源码适用 [PolyForm Noncommercial 1.0.0](https://polyformproject.org/licenses/noncommercial/1.0.0/)；机械模型、PCB、原理图、图片、文档、Unity 工程和其他非软件资料适用 [CC BY-NC-ND 4.0](https://creativecommons.org/licenses/by-nc-nd/4.0/)。
- SE、SW、STEP/STP 及其他三维模型仅允许个人或学校进行非商业学习、评审和研究；未经著作权人书面许可，不得用于加工、打样、制造、销售、量产、投标、产品交付、商业宣传、改作或再授权。
- 第三方组件、模板、元件库、外购件模型和工具仍受其原始许可证约束；本声明不扩大作者对第三方内容的权利。

商业使用、产品化、公司内部长期使用、转让或排他性授权需另行取得著作权人的书面许可。
