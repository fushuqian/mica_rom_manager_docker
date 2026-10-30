# Mica ROM Manager（Web / Docker 版）

在 NAS 或任意 Linux 主机上用 Docker 跑起来的 **ROM 管理工具**：扫描 ROM 目录、按内容指纹去重、识别平台、补全元数据与封面、筛选与批量编辑，并可选接入 OpenAI 兼容接口做 **AI 元数据刮削**。

界面与操作逻辑沿用 Windows 桌面版（WPF）的设计：顶部工具栏 + 左侧平台栏 + 筛选条 + 表格 + 双击编辑 + 右键菜单。本项目是**独立实现**，不复用桌面版的数据库与安装包。

> 浏览器直接试玩（RetroArch / EmulatorJS）是规划中的下一阶段；本期已预留 ROM 流接口、平台 `play_core` 字段和 `/play/{id}` 页面骨架。

---

## 功能

| 模块 | 能力 |
| --- | --- |
| 库管理 | 添加容器内目录、递归扫描、平台识别、路径 + SHA-256 指纹去重、缺失文件标记 |
| 列表 | 服务端分页、排序、平台侧栏、关键词/平台/状态/区域/语言/类型/标签/收藏/缺失筛选 |
| 编辑 | 单条编辑、行内改平台/区域/语言/状态、点星评分、收藏、批量编辑 |
| 封面 | 上传 jpg/png/webp（≤10MB），列表缩略图，替换/移除，孤儿清理 |
| 整理 | 按「标题 (区域)(语言)(特注)」批量重命名，先预览后执行 |
| AI 刮削 | 本地文件名解析 + AI 补全 + 可选 DeepSeek 联网搜索，逐字段预览确认后写库 |
| 数据 | CSV 导出、数据库备份/恢复（VACUUM INTO）、补算指纹、刷新缺失状态 |
| 运维 | 单密码登录、登录限流、健康检查、文件日志、非 root 运行、`/data` 持久化 |

## 快速开始

```bash
cp .env.example .env
# 编辑 .env：MICA_PASSWORD、ROM_ROOT、PUID/PGID
docker compose up -d --build
```

打开 `http://<NAS-IP>:8088`，用 `.env` 里的 `MICA_PASSWORD` 登录。

首次使用：

1. 进入「扫描 / 导入」→ 添加库目录（容器内路径，例如 `/roms` 或 `/roms/nes`）；
2. 选择库 → 开始扫描；
3. 回到「游戏库」筛选/编辑；
4. 需要 AI 时到「设置 → AI」填 BaseUrl / API Key / 模型，再在「AI 刮削」里预览并应用。

> compose 默认把 ROM 目录**只读**挂载到 `/roms`。需要「整理文件名」或导入复制时，把 `compose.yaml` 里的 `:ro` 改成 `:rw`，并确保 `PUID/PGID` 对目录有写权限。

## 配置（环境变量）

| 变量 | 必填 | 默认 | 说明 |
| --- | --- | --- | --- |
| `MICA_PASSWORD` | 是 | — | 访问密码，至少 8 位；缺失时容器启动失败 |
| `MICA_DATA_DIR` | 否 | `/data` | SQLite、封面、备份、日志、Data Protection 密钥 |
| `MICA_LIBRARY_ROOTS` | 否 | 空 | 逗号分隔的白名单；设置后网页只能在该目录下添加库 |
| `MICA_AI_API_KEY` | 否 | 空 | 覆盖网页里保存的 AI Key（适合用 secrets 管理） |
| `PUID` / `PGID` | 否 | `1000` | 容器运行用户；入口脚本会修正 `/data` 属主 |
| `TZ` | 否 | `Asia/Shanghai` | 时区 |
| `MICA_PORT` | 否 | `8088` | compose 对外端口 |

数据卷：

```
./data/
├── mica.db          # SQLite（WAL）
├── covers/          # 封面文件
├── backups/         # 数据库备份
├── keys/            # Data Protection 密钥环（用于加密 AI Key）
├── logs/            # app-YYYYMMDD.log
└── trash/           # 预留
```

## AI 刮削

- 兼容 OpenAI `POST /chat/completions`：填入 BaseUrl（如 `https://api.deepseek.com/v1`）、API Key、模型名即可。
- Key 使用 ASP.NET Data Protection 加密后写入本地 SQLite；页面只显示掩码，日志不记录 Key。
- 可选「联网搜索」：复用同一个 Key，仅支持 DeepSeek 官方端点（`api.deepseek.com`），按成功请求计费，默认关闭。
- 刮削流程：本地文件名解析 → （可选）搜索 → 模型补全 → 预览（逐字段勾选，默认只补空）→ 应用。
- 提示词可在「设置 → 提示词」整段替换，变量用 `{{FileName}}` 这类占位符，写错会原样保留。

## 备份与升级

- 设置 → 数据维护 → 创建备份（`VACUUM INTO`），可下载/恢复；恢复前会自动再备份当前库。
- 升级：`docker compose build --no-cache && docker compose up -d`；数据在 `./data` 不受影响。
- 数据库迁移在启动时自动执行，每个版本包在事务里；迁移前建议先备份。

更细的 NAS 部署、反代与排障见 [docs/deployment.md](docs/deployment.md)，界面规范见 [docs/ui-spec.md](docs/ui-spec.md)。

## 安全说明

- 单密码登录，Cookie `HttpOnly` + `SameSite=Lax`，登录接口按 IP 限流（5 次/分钟）。
- 所有文件访问限制在已登记库根或数据目录内；ROM 流/下载接口会校验路径并支持 Range。
- 「从库中移除」只删记录，不动 ROM 文件；不提供永久删除文件的入口。
- 建议只在局域网使用；需要暴露到公网时，请在 NAS 反向代理上启用 HTTPS，并设置强密码。

## 本地开发

需要 .NET 10 SDK。

```bash
# 运行（数据写到 src/MicaRomManager.Web/data）
MICA_PASSWORD=dev-password dotnet run --project src/MicaRomManager.Web --urls http://127.0.0.1:5161

# 测试
dotnet test

# 构建镜像
docker build -f deploy/Dockerfile -t mica-rom-manager:dev .
```

项目结构：

```
src/MicaRomManager.Core/   # 领域层：SQLite、扫描、指纹、整理、AI（无 UI 依赖）
src/MicaRomManager.Web/    # ASP.NET Core + Blazor Server + 极简 REST
tests/                     # xUnit 单测与 Web 集成测试
deploy/                    # Dockerfile、entrypoint
compose.yaml / .env.example # 一键部署
docs/                      # 部署、界面规范
```

## 路线图

- **M5 AI 助手**：右侧聊天面板 + 工具调用 + 确认卡（参考桌面版设计）。
- **M6 浏览器试玩**：自托管 [EmulatorJS](https://github.com/emulatorjs/emulatorjs)（RetroArch 核心的 WASM 前端），`/play/{id}` 加载核心并通过 `/api/games/{id}/rom` 以 Range 流式读取 ROM；存档存浏览器 IndexedDB。注意其 GPL 许可与镜像分发合规。
- **M7 可选服务端串流**：Xvfb + RetroArch + WebRTC，适合低配客户端，另行评估。

参考：[RetroArch Web Player（Emscripten 构建说明）](https://github.com/libretro/RetroArch/blob/master/pkg/emscripten/README.md)。

## 已知限制

- 只支持单实例、单密码；不支持多用户与权限分级。
- 数据库与 Windows 桌面版**不保证兼容**，暂不提供导入器。
- 扫描按批入库；取消时保留已入库批次，未处理文件下次扫描继续。
- 本期为「就地登记」：扫描已挂载目录，暂不提供从库外复制/移动入库（可先手动放进 ROM 目录再扫描）。
- 列表列固定（平台/标题/评分/区域/语言/特注/类型/年份/光碟/格式/状态/次数/时长/最近/大小/标签），列显隐与拖放导入未实现。
- 封面只有列表缩略图，没有独立的卡片网格视图。
- 大于 128MB 的 ROM 使用「大小 + 首/中/尾各 1MB」采样指纹（带 `fast:` 前缀），极端情况下可能误判重复。

## License

MIT，见 [LICENSE](LICENSE)。
