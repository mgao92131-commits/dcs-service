# dcs-service 数据访问接口

本文档描述当前流式协议。服务只读，监听地址固定为 `127.0.0.1`，没有应用层 API Key。不要把端口暴露到 LAN；如需远程访问，应由外部隧道或反向代理负责。

## 通用约定

接口：

```text
GET /health
GET /api/v1/info
GET /api/v1/tag?tag=<TAG>
GET /api/v1/history?tag=<TAG>&from=<FROM>&to=<TO>
GET /api/v1/events?from=<FROM>&to=<TO>
GET /api/v1/events?afterTime=<TIME>&afterFracSec=<N>&afterOrd=<N>&sourceGeneration=<GENERATION>&to=<TO>
```

`from`、`to`、`afterTime` 都是 Historian/Event Journal 的 source-local 时间，不带 `Z`，也不带 offset。默认时区是 `China Standard Time`，实际值以配置为准。时间范围为半开区间 `[from,to)`，必须满足 `to > from`。

查询参数值应进行 URL percent-encoding。未知参数通常没有业务意义；Event 的旧分页参数 `limit` 会被拒绝，服务不再提供分页协议。

JSON 错误格式：

```json
{"ok":false,"error":{"code":"invalid_request","message":"..."}}
```

在 HTTP 响应头已经发出前发生的参数、连接、Tag 或 Event Journal 校验失败，会返回正常的 JSON 错误状态。流开始后不能再切换成 JSON；此时服务记录错误、关闭连接且不写 chunked terminating chunk。客户端必须丢弃部分文件并重新请求整个范围。

## History

### 请求

```text
GET /api/v1/history
?tag=TI-021007_AI1_PV.CV
&from=2026-08-01T00:00:00
&to=2026-09-01T00:00:00
```

返回一个完整 CSV，不按 24 小时或 sample 数截断。History 响应不会返回行数，也没有分页游标。

响应头包括：

```text
Content-Type: text/csv; charset=utf-8
Transfer-Encoding: chunked
Content-Disposition: attachment; filename="history_<safe-tag>_<from>_<to>.csv"
X-DCS-Tag: TI-021007_AI1_PV.CV
X-DCS-Source-TimeZone: China Standard Time
X-DCS-From: 2026-08-01T00:00:00.0000000
X-DCS-To: 2026-09-01T00:00:00.0000000
```

CSV Header 只出现一次：

```csv
Timestamp,Value,DataType,DeltaVStatus,ArchiveStatus,SequenceNo,IsHistoryHole,IsCRHole,IsManuallyDeleted,IsManuallyInserted
```

时间戳按升序输出，CSV 为无 BOM UTF-8，数字使用 invariant culture，字段按 CSV 规则转义。

服务端处理方式：

1. 获得一个 History 并发槽；
2. 解析 Tag、建立一个独立的 `DvCHReadConnection`，确认 Tag 状态为 `HistoryTagOK`；
3. 将客户端范围切成 `StreamWindowMinutes`（默认 60 分钟）的内部窗口；
4. 每个窗口调用 `readRaw(maxSamples=ReadChunkSamples)`；
5. 如果返回 `dataTruncated=true`，按时间二分，先处理 left，再处理 right，直到得到完整 segment；
6. 对当前 segment normalize/dedup，跳过与上一条已输出记录完全相同的边界重复；
7. 将当前 batch 立即写入 HTTP CSV 流，然后释放该 batch；
8. 全部窗口完成后释放连接和并发槽。

`ReadChunkSamples` 只是一次 DeltaV `readRaw` 的读取上限，`StreamWindowMinutes` 只是内部性能参数，两者都不是 API 返回数量上限。

## Event

### Range 模式

```text
GET /api/v1/events
?from=2026-08-30T08:00:00
&to=2026-08-30T09:00:00
```

服务返回 `[from,to)` 内全部 Event，顺序为：

```text
(Date_Time, FracSec, Ord) ASC
```

没有 `TOP`、客户端行数参数、分页状态或下一页 Header。

### Cursor 模式

```text
GET /api/v1/events
?afterTime=2026-08-30T08:55:00.123
&afterFracSec=123
&afterOrd=456
&sourceGeneration=APP%7C2026-08-30T00:00:00.000
&to=2026-08-30T09:00:00
```

Cursor 是数据库同步 checkpoint，不是分页工具。`to` 必须提供，服务只查询 Cursor 之后直到这个固定边界的数据，不会无限读取到当前最新值。`sourceGeneration` 必须与之前响应中的 generation 一致。

响应头包括：

```text
Content-Type: text/csv; charset=utf-8
Transfer-Encoding: chunked
Content-Disposition: attachment; filename="events_<from>_<to>.csv"
X-DCS-Source-TimeZone: China Standard Time
X-DCS-Source-Generation: APP|2026-08-30T00:00:00.000
X-DCS-To: 2026-08-30T09:00:00.000
```

CSV Header 只出现一次：

```csv
DateTime,FracSec,Ord,EventType,EventSubType,Category,Area,Node,Unit,Module,ModuleDescription,Attribute,State,EventLevel,Desc1,Desc2,IsArchived
```

每一行都包含 `DateTime,FracSec,Ord`。客户端完成一次同步后，可从 CSV 最后一行保存这三个字段以及 `X-DCS-Source-Generation`，作为下一轮 Cursor 请求的 checkpoint。

Event 数据由 `SqlDataReader.Read()` 逐行转换为 domain model 并直接写入 CSV，不建立整个结果的 `List<EventRecord>`。

### 完整性保护

Range 和 Cursor 请求都会在查询前后检查运行时状态。以下情况 fail-closed：

- `JournalProperties.IsFull`；
- `EJOverflow` 无法确认为空或确实包含记录；
- 查询期间 `sourceGeneration` 变化；
- retention 已经越过请求的起点或 Cursor；
- Cursor 超前于当前 Journal；
- Cursor 字段存在 NULL，无法建立可靠顺序。

如果检查在 CSV 已经开始输出后失败，响应不会发送 terminating chunk，客户端必须把本次下载判定为失败。

## Chunked 传输语义

成功响应的最后部分是：

```text
0\r\n
\r\n
```

服务不会为大范围数据预先计算 `Content-Length` 或行数。CSV writer 保持缓冲，在 batch 或约 1000 行时 flush，不会每一行 flush。

文件名中的 `/`、`\`、`:`, `*`, `?`, `"`, `<`, `>`, `|` 及控制字符会被替换。

## 并发和超时配置

```ini
[Concurrency]
HistoryMaxConcurrent=2
EventMaxConcurrent=4
RequestQueueLimit=32

[Timeout]
ProviderSlotWaitSeconds=60
SocketReadSeconds=60
SocketWriteSeconds=120
```

一个下载从获得槽开始到成功结束或失败关闭，始终占用一个对应的 Provider 槽。大范围下载没有固定的总请求 deadline；`SocketWriteSeconds` 用于终止长期不读取响应的慢客户端，并确保连接、Provider 和并发槽最终释放。

## 客户端示例

PowerShell：

```powershell
$tag = [Uri]::EscapeDataString("TI-021007_AI1_PV.CV")
$uri = "http://127.0.0.1:18080/api/v1/history?tag=$tag&from=2026-08-01T00%3A00%3A00&to=2026-09-01T00%3A00%3A00"
Invoke-WebRequest $uri -OutFile history.csv
```

Python：

```python
import csv
import requests

params = {
    "tag": "TI-021007_AI1_PV.CV",
    "from": "2026-08-01T00:00:00",
    "to": "2026-09-01T00:00:00",
}
with requests.get("http://127.0.0.1:18080/api/v1/history", params=params, stream=True) as response:
    response.raise_for_status()
    with open("history.csv", "wb") as output:
        for block in response.iter_content(1024 * 1024):
            if block:
                output.write(block)
```

客户端应使用 HTTP 库解析 chunked framing，不要把 chunk 长度行写入 CSV 文件。只有请求正常结束并收到 terminating chunk，才将临时文件改名为最终文件。
