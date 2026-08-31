# HistoryJuno

HistoryJuno 0.3.0 是一个加载到 HistoryVulcan 并显示在 HistoryAurora 主页面中的
Sub2API 账号与服务控制面板。`Juno` 页面提供启动、代理重连、关闭和刷新，
并在账号管理/服务状态两个子页间切换；账号管理支持按分组和状态检索。
独立的“账号导入”页复用 Aurora 通用 JSON 来源组件，可选择导入分组，并在导入后
自动绑定本机唯一的活动代理 IP，不创建独立网页。

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
easyTOOL/SU2API 查找 sub2api-tool.ps1 与 proxy-reconnect.ps1，供停止和代理连接动作使用。
启动动作直接分阶段启动 Ubuntu Docker 服务和 Sub2API compose，拒绝重复启动，并以
8080/9090 实际监听作为成功条件。账号、分组、代理和导入操作走本机 Sub2API 管理接口；
凭据目录可由 SUB2API_DEPLOY_ROOT 指定，默认使用 %USERPROFILE%/Projects/sub2api-deploy。
账号 JSON、管理密码、令牌和原始响应只在内存及 127.0.0.1 回环中使用，不进入日志或页面数据。
