# 部署说明（NAS / Docker）

## 1. 前置条件

- x86_64 或 arm64 的 Linux 主机，Docker 20.10+ 与 Compose v2；
- 一个放 ROM 的目录（建议单独数据集）；
- 主机端口（默认 `8088`）未被占用。

镜像基于 `mcr.microsoft.com/dotnet/{sdk,aspnet}:10.0`，不需要 Docker Hub；国内网络可直接拉取。

## 2. 部署

```bash
# 在项目根目录
cp .env.example .env
vi .env
```

至少修改：

```ini
MICA_PASSWORD=一个至少 8 位的密码
ROM_ROOT=/mnt/roms              # 宿主机 ROM 目录（只读挂载）
PUID=1000                        # 拥有 ROM 目录的用户
PGID=1000
```

然后：

```bash
docker compose up -d --build
docker compose logs -f mica      # 看到 "Now listening on: http://[::]:8080"
```

访问 `http://<NAS-IP>:8088`。

## 3. 权限

容器以 `PUID:PGID` 运行。`/data` 由 root 创建时，entrypoint 会自动 `chown`；ROM 目录建议：

```bash
chown -R 1000:1000 /mnt/roms
```

只读挂载（`:ro`）时：

- 扫描、编辑元数据、封面、AI 刮削、CSV、备份都正常；
- 「整理文件名」「复制入库」会被禁用（或执行时失败），需要把 compose 中的 `:ro` 改成 `:rw`。

## 4. 数据与备份

`./data` 是唯一持久化目录，包含：

| 路径 | 内容 |
| --- | --- |
| `mica.db` | SQLite（WAL；同时存在 `-wal`/`-shm` 属正常） |
| `covers/` | 封面原图 |
| `backups/` | `VACUUM INTO` 生成的备份 |
| `keys/` | Data Protection 密钥环（AI Key 解密依赖它，**不要丢**） |
| `logs/` | 按天滚动日志，保留 7 天 |

备份策略：设置页「创建备份」或直接 `tar czf mica-data-$(date +%F).tgz data/`（停机时最稳）。

恢复：设置页上传 `.db`，或停机后替换 `data/mica.db` 并删除 `mica.db-wal`/`mica.db-shm`。

## 5. 反向代理（可选）

NAS 自带反代通常支持 WebSocket；Blazor Server 需要：

- 允许 `Upgrade`/`Connection` 头；
- 关闭响应缓冲（`proxy_buffering off;`）；
- 超时 ≥ 120 秒（SignalR 心跳间隔较长）。

Nginx 示例：

```nginx
location / {
    proxy_pass http://127.0.0.1:8088;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_buffering off;
    proxy_read_timeout 300s;
}
```

程序已启用 `X-Forwarded-*` 处理；HTTPS 终止在反代时，登录 Cookie 会按请求协议自动使用 `Secure`。

## 6. 排障

| 现象 | 排查 |
| --- | --- |
| 容器启动即退出，日志提示 MICA_PASSWORD | `.env` 未设置或少于 8 位 |
| 页面能开但登录后刷新掉线 | 反代未透传 Cookie/WebSocket，或 `SameSite` 被跨站代理破坏 |
| 添加库提示目录不存在 | 路径是**容器内**路径；确认 compose 挂载与大小写 |
| 扫描 0 个文件 | 扩展名不在平台格式内；到「设置 → 词典库/平台」补充，或确认目录层级 |
| 整理文件名失败 | ROM 目录是 `:ro`，或 PUID 无写权限 |
| AI 401 | Key 错误或 BaseUrl 不含 `/v1`；先点「测试连接」 |
| AI 刮削很慢 | 每款一次模型调用；缩小选择范围或关闭「联网搜索」 |
| 备份恢复后仍看到旧数据 | 重启容器（`docker compose restart`），并确认没有旧 `-wal` 文件 |

查看日志：

```bash
docker compose logs -f mica
tail -f data/logs/app-$(date +%Y%m%d).log
```

## 7. 升级

```bash
git pull            # 或替换源码
docker compose build --no-cache
docker compose up -d
```

数据库迁移在启动时自动执行（事务内）；升级前建议先创建备份。
