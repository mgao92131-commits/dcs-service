# 原生实时参数 API（1.3.0）

实时、历史和事件由一个 DcsDataService.exe 提供，目标为 Windows 7、.NET Framework 3.5、x86，监听 127.0.0.1。

## 部署和构建

DCS 本机安装目录固定为 `C:\DeltaV\bin`。不使用共享目录、父目录、环境变量覆盖或目录配置。Historian 托管程序集通过 CLR 解析事件从安装目录加载；原生运行库按绝对路径加载。正式 bin 只包含 DcsDataService.exe，不复制厂商 DLL，不包含测试工具。

在 DCS 电脑执行 build-net35-x86.bat，然后按原来的命令启动：

```bat
bin\DcsDataService.exe serve --config config.ini
```

保留原有 Historian、Events、Api 配置。没有 Realtime 节时默认启用；旧配置若显式 Enabled=false，仍表示关闭，升级时删除该设置或改成 true。DeltaVBinDirectory、WorkerPath、MaxConcurrent、WorkerTimeoutSeconds 已不参与运行。

可选设置仅保留以下三项；不配置就使用默认值。

```ini
[Realtime]
Enabled=true
MaxBatchTags=16
SlotWaitSeconds=5
```

批量上限范围 1–64，排队等待范围 1–300 秒。

## 接口

- GET `/api/v1/realtime?tag=SY-218%2FAI1%2FPV.CV`
- POST `/api/v1/realtime/batch`，Content-Type 为 application/json。

```json
{"tags":["SY-218/AI1/PV.CV","SY-218/AI1/PV.CV"]}
```

使用真实完整参数路径；不经过 OPC，不根据 Historian 标签猜测路径。保留输入顺序和重复项，每点分别返回成功或错误。全部点读完的批量返回 HTTP 200；ok 仅在全部成功时为 true。complete、计数和既有 protocolVersion:1 响应字段保留兼容性，但已没有进程通信协议。

value 保留数据类型和精度，valueText 保留厂商格式，另外返回 valueType、nativeDataType、nativeTypeName。observedAt 是配置时区内的观测时间；sourceTimestamp 和 quality 保持 null。批量顺序采样，不是同时刻快照。

## 生命周期与错误

原生调用在主服务内同一条专用线程上执行，首次读取时初始化一次。健康检查不初始化原生环境。每次读取主动释放路径、数据项、转换文本和 Locate 返回的角色对象。运行库保持加载直到进程退出，不卸载可能仍有厂商后台线程的 DLL。

支持的 ABI 仍为已验证的 DeltaV 10.3.4.5354；bss.dll、Sec.dll、oss.dll、RtStringTable.dll、roc.dll 校验 SHA-256。初始化失败不会不断重试部分加载的运行库，修正安装后应重启服务。

单点不存在返回 404 parameter_not_found；输入错误返回 400；排队超时返回 503 service_busy；环境或初始化失败返回 503 realtime_unavailable / realtime_abi_mismatch；读取失败返回 realtime_read_failed 或具体点级原生错误。批量中可恢复的点级错误不阻止其他点读取。

已删除子进程、临时请求文件、进程监控、杀进程超时。同步原生调用不能安全强制中断：阻塞时保留读取线程及并发槽位，不启动替代线程。HTTP 客户端超时不代表底层调用已取消。严重原生故障可能影响整个服务。

## 现场验证

在正常 WatchIt 用户环境执行：

1. RUN-REALTIME-API-TEST.cmd：单点、混合批量及错误输入，可补充未入历史点和其他类型。
2. RUN-STABILITY-TEST.cmd：连续和并发请求，记录主进程内存、句柄，重新验证原生对象释放。
3. RUN-HISTORY-EVENT-CHECK.cmd：历史、事件回归，site-config.ini 提供现场数据源。
4. RUN-OFFLINE-CHECKS.cmd：模拟运行库，不读取现场参数，不能代替真实测试。

结果在 TestResults。1.2.0 的现场通过记录不代表 1.3.0 已通过。生产仅交付一个业务 EXE 和原有配置，不需要测试工具。
