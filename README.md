# Jellyfin 魔改版（服务端）

> **这是 [jellyfin/jellyfin](https://github.com/jellyfin/jellyfin) 的个人 fork，不是官方版本，也不打算给上游提 PR。**
> 所有改动都在 `videotools` 分支上。

一句话概括：给 Jellyfin 加了**转码 / 掐头去尾**、**后台任务队列**、**剪辑预览抽帧**、**按文件大小、比特率排序**。

> 🤖 **本仓库全部代码由 AI 编写。**
> 作者是一位看不懂代码的天才程序员——需求、交互、审美和验收是他，敲键盘的是 AI。

配套前端：[Dimerry/jellyfin-web](https://github.com/Dimerry/jellyfin-web)（分支 `videotools`，界面上的按钮和对话框都在那边）。

---

## 一、功能改动（按模块）

### 1. 视频工具 API — `Jellyfin.Api/Controllers/VideoToolsController.cs`

新增一整套 `/VideoTools` 接口，全部需要**管理员**权限。前端菜单调用这些接口。

**转码**

- `POST /VideoTools/{itemId}/Transcode?codec=h265|av1&bitrateKbps=3000&resolution=1080p|4k`
  单条入队。
- `POST /VideoTools/Transcode` + body `["guid","guid"]`
  批量入队，返回 `{ Added, Skipped, Jobs[], SkippedNames[] }`；没有本地文件 / 不是视频的条目按名字回报跳过。
- 编码策略：
  - H.265 走 Intel QSV 硬解硬编（`hevc_qsv`）；
  - AV1 走 `libsvtav1` preset 8（UHD 770 没有 AV1 硬编，只能软编）；
  - 音频统一 AAC 192k，文本字幕转 `mov_text`，位图字幕跳过，加 `+faststart`。
- 分辨率默认 1080p、可选 4K，**只缩不放**（源比目标小就保持原尺寸）。

**剪辑**

- `POST /VideoTools/{itemId}/Cut?startSeconds=2&endSeconds=3&rotation=0|90|180|270`
- `-ss` 输入端 + `-c copy` 流复制，秒完成、无损。
- 旋转用 `-display_rotation`（放在 `-i` 之前）重写 display matrix，仍然是流复制，不重编码。

**剪辑预览（抽帧）**

- `GET /VideoTools/{itemId}/Frame?timeSeconds=&maxWidth=480&rotation=0|90|180|270` → 单帧 JPEG。
- 不进任务队列，单帧 0.1~0.3s；`SemaphoreSlim(2)` 限流，避免拖慢实时转码。
- 按 `<itemId>_<源文件mtime>_<宽>_<旋转>_<时间>` 缓存到临时目录，命中约 2ms；超过 400 个自动清到 200 个。

**任务队列**

- `GET /VideoTools/Jobs`、`GET /VideoTools/Jobs/{id}`
- `DELETE /VideoTools/Jobs/Finished`：清掉已完成 / 失败 / 已取消，保留运行中和排队。
- `POST /VideoTools/Jobs/{id}/Retry`：只允许失败任务，沿用原参数、输出名自动递增。
- `POST /VideoTools/Jobs/{id}/Cancel`：排队中直接移除；运行中会终止 ffmpeg 并删掉半成品。
- `GET /VideoTools/Jobs/{id}/Stream`：已完成任务的输出流地址，支持 Range，原生 `<video>` 用 `?ApiKey=<token>` 认证。
- **文件任务串行执行**：同一时间只跑一个转码/剪辑，其余 `Queued` 并带队列位置；播放时的实时转码不经过这个队列，不受限制。
- 队列清空后延迟 **2 秒**触发一次媒体库扫描（2 秒内又来新任务则取消本次扫描，等下一次清空）。

### 2. 输出文件命名（自用规则，有点激进）

- **转码**：新文件沿用源文件主名 `<源名>.mp4`；成功后源文件改名为 `<源名>.old.<原扩展名>` 保留（重名用 `名字 (1).old.mp4`）。
- **剪辑**：新文件直接顶替源文件 `<源名>.<原扩展名>`，源文件同样改名成 `.old` 保留。
- 两种操作都先写隐藏临时文件 `.<源名>.videotools.*`，成功后才做改名交换，**中途失败不会破坏源文件**；失败/取消会删临时文件。

### 3. 转码稳定性补丁

- **帧率修正**：探测 `avg_frame_rate` / `r_frame_rate`，转码时固定输出 `-r <真实帧率>`（吸附到 23.976 / 24 / 25 / 29.97 / 30 / 48 / 50 / 59.94 / 60 / 100 / 119.88 / 120）。
  原因：很多手机/remux 文件的 `r_frame_rate` 是假的（30fps 报 `120/1`），编码器按假帧率分配码率预算，导致指定 3000k 实测只有 750k 且画面卡顿。
- **解码回退**：`h264 / hevc / av1 / vp8 / vp9 / mpeg2video / vc1 / mjpeg` 以外的一律走软件解码（`scale=` 而不是 `scale_qsv`）；硬件路径失败会自动用软件参数重试一次。
  修的是 `.avi`(mpeg4/DivX) 直接失败的问题：QSV 解不了 mpeg4，会静默回退成 yuv420p，而 `scale_qsv` 只吃 QSV surface → 滤镜链整体失败。
- 剪辑时长优先用 ffmpeg 探测的真实时长，探测不到才用数据库里的 `RunTimeTicks`（刚剪过的文件数据库时长是旧的）。

### 4. 排序增强

- `Jellyfin.Data/Enums/ItemSortBy.cs` 新增 `Size = 30`。
- `OrderMapper.cs` 映射 `ItemSortBy.Size → e.Size`。
- 新增 `SizeComparer`、`BitrateComparer` 内存排序比较器（自动注册）。

---

## 二、构建与运行

环境要求：

- .NET SDK **10.0**（仓库 `global.json` 要求 10.0.0）
- `ffmpeg`（转码/剪辑/抽帧全靠它）
- 想用 QSV 硬编：Intel 核显 + `/dev/dri/renderD128` 权限（把运行用户加进 `render`、`video` 组，并用 `sg render -c ...` 启动）

```bash
dotnet restore Jellyfin.Server/Jellyfin.Server.csproj
dotnet build Jellyfin.Server/Jellyfin.Server.csproj -c Release --no-restore

dotnet run --project Jellyfin.Server -c Release -- \
  --datadir /path/to/data \
  --webdir /path/to/jellyfin-web/dist
```

`--webdir` 指向配套前端 [Dimerry/jellyfin-web](https://github.com/Dimerry/jellyfin-web) 的 `dist`（`npm run build:production` 产物），否则界面上看不到这些功能。

> 默认端口 8096；作者本机跑在 8196，纯属习惯。

---

## 三、免责声明

- 个人自用 fork，代码质量按"能跑就行"标准，**没有测试覆盖**，不保证数据安全。
- 转码/剪辑会**移动和改名你的媒体文件**（改成 `.old`），请先在小范围试。
- 与 Jellyfin 官方无关，问题不要提到上游。

## 四、许可

沿用上游 [GPLv2](LICENSE)。
