# AI 操作功能规则

AI 只通过 `data/ai` 下的文件操作日程和标签，不直接修改 `schedules.json` / `tags.json`。

日期时间为本地时间，格式 `yyyy-MM-dd`、`HH:mm`。

## 1. 文件与触发

| 用途 | 文件 | 说明 |
|---|---|---|
| 读取数据 | `<程序目录>/data/ai/snapshot.json` | 程序生成，只读 |
| 提交命令 | `<程序目录>/data/ai/inbox.json` | 整体覆盖；先写 `inbox.tmp` 再改名 |
| 查看结果 | `<程序目录>/data/ai/outbox.json` | 程序写回，只读 |

- 只有用户点主界面「刷新」才处理 `inbox`；程序不轮询、不监听、启动不执行。
- AI 总开关关闭时，不读、不执行、不写 `outbox`；`inbox` 和 `snapshot` 保留。`snapshot` 存在不代表开关已开。
- 数据阻断时跳过导出、保留旧快照，`generatedAt` 可能陈旧。
- 单批最多 30 条命令、文件不超过 50 KB，超出则整批不执行。

## 2. 看懂 snapshot

`snapshot.json` 顶层是 `generatedAt`、`tags`、`schedules`，另有 `schemaVersion`、`notice` 无需处理；`schedules` 存日程母项，重复规则不展开。

| 字段 | 说明 |
|---|---|
| `id` | 日程编号；`targetId` 用它 |
| `seriesId` | 同系列分段共用；命令不使用 |
| `tag` / `tagId` | 标签名快照 / 标签 ID；标签删除后仍可能保留 |
| `doneDates` | 已完成的发生日，`yyyy-MM-dd` |
| `sound` | 日程为 `{"preset":"文件名"}`；标签为字符串 |
| `recurrence` | 重复规则，子字段见第 4 节 |
| `remindEnabled` / `isTodo` | 提醒开关 / 待办标记 |
| `tags` | 标签数组，元素含 `id`、`name`、`bgColor`、`fontColor`、`sound` |
| `title` / `date` / `time` / `note` / `advanceMinutes` / `bgColor` / `fontColor` | 与第 4 节的字段规则一致 |

过期要自己算（snapshot 里没有 `expiresAt`），口径是「不晚于当前时间即过期」：

```text
一次性：date + time <= 现在 => 已过期
重复：until 为空 => 永不过期
      until + time <= 现在 => 整条已过期
```

已经过期的日程只能 `setDone` 补记或取消打勾、`setTodo` 切换待办，不能改内容、不能删。

## 3. 命令格式

```json
{
  "commands": [
    {
      "seq": 1,
      "type": "schedule",
      "op": "create",
      "title": "季度复盘会",
      "date": "2026-11-03",
      "time": "14:30"
    }
  ]
}
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `seq` | 整数 | 是 | 从 1 连续递增，回执带回 |
| `type` | 字符串 | 是 | `schedule` 或 `tag` |
| `op` | 字符串 | 是 | 见下 |
| `targetId` | 字符串 | 改、删、setDone、setTodo 必填 | 目标 `id` |

```text
日程：create / update / delete / setDone / setTodo
标签：create / update / delete
```

其他组合整条失败。

## 4. 字段与写入规则

| 字段 | 规则 |
|---|---|
| `title` | 1–12 字，超 12 字整条失败 |
| `date` | 起始日期，1900–2100 年，不得早于今天 |
| `time` | `HH:mm`，24 小时制 |
| `note` | 不超过 500 字 |
| `advanceMinutes` | 0–60；新建不填按 0，超过 60 收敛到 60，负数整条失败 |
| `tagId` | 必须是标签 `id`，不接受名字 |
| `bgColor` / `fontColor` | `#RRGGBB` |
| `sound` | `{"preset":"文件名"}`，文件须在 `data\sound\` 里；新建不填即静音 |
| `remindEnabled` / `isTodo` | 布尔；新建不填为 `true` / `false` |
| `recurrence` | 见下 |

禁止下发 `id`、`seriesId`、`doneDates`；完成记录只能走 `setDone`。

清空：`tagId`、`bgColor`、`fontColor`、`note` 传 `""`；`sound` 传 `{"preset":""}`；`recurrence` 传 `{"enabled":false}`。

各操作的必填：

```text
日程 create：title、date、time，其余可选
日程 update：targetId，且至少给一个字段；未给的保持不变
日程 delete：targetId
日程 setDone：targetId、occurrenceDate、done，occurrenceDate 须是实际发生日
日程 setTodo：targetId、isTodo
标签 create：name 为 1–6 字；bgColor / fontColor / sound 可选，sound 省略取程序默认铃声
标签 update：targetId，且至少给一个字段；空字符串清除颜色或铃声
标签 delete：targetId
```

**边界规则。** 提醒时刻 = `date + time − advanceMinutes`，必须晚于当前时间 1 分钟以上；否则失败「当前时间已超过日程提醒或提前提醒」。今天的日程最容易触发。

重复规则子字段：

| 子字段 | 含义 |
|---|---|
| `enabled` | `false` 即一次性 |
| `yearly: {month, day}` | 每年某月某日 |
| `monthly: {day}` | 每月某日 |
| `monthlyLastN: {n}` | 每月倒数第 n 天，1–7 |
| `monthlyLastWeekday: {weekday}` | 每月最后一个周几，0=周日 |
| `weekly: {weekdays: [0-6]}` | 每周，0=周日 |
| `until` | 截止日期；不能早于系列最早起始日；空为永久 |

`enabled=true` 时至少选一种方式，否则整条失败「重复规则不完整」。

改动的连带范围：

- 改标签的名字、颜色或铃声，所有引用它的日程跟着变，包括过期日程；日程上的 `tagId` 不用改。
- 设 `tagId` 会套用该标签的预设颜色与铃声，显式填的值优先。
- `tagId` 不变时不重套预设，改其他字段不会覆盖已有颜色与铃声。
- 删标签只删模板，日程保留原 `tagId` 与名字。
- 改已生效重复的 `bgColor` / `fontColor` / `sound`，整条系列同步，含历史段。
- 改 `remindEnabled` / `isTodo` 只影响目标那一条。
- 已生效重复下发 `date` 不落库，其余字段照改。
- 删除已生效重复只停未来、保留历史；删一次性或未生效重复删整条。

## 5. outbox 回执

`outbox.json` 每次整体覆盖，只保留最近一批。

```json
{
  "executedAt": "2026-11-01 10:42:00",
  "inbox": [ ... 本批原始命令 ... ],
  "results": [
    { "seq": 1, "ok": true },
    { "seq": 2, "ok": false, "reason": "目标已过期" }
  ]
}
```

| 字段 | 说明 |
|---|---|
| `executedAt` | 本批处理时间 |
| `inbox` | 本批原始命令 |
| `inboxText` | 解析失败时的原始文本；此时不写 `inbox` |
| `results` | 与 `commands` 按 `seq` 对齐 |
| `ok` / `reason` | 成功 / 失败原因 |
| `newId` | 新建或分段后的新编号；分段时原 `id` 仍指历史段，后续改未来段用它 |

整批未执行时，`batchSkipped` 为 `true`、带 `reason`、`results` 为空数组。原因只有四种：`输入超过 50 KB`、`格式错误`、`没有命令`、`命令条数超过三十条上限`。

程序写 `outbox` 后清空 `inbox`；单条失败、解析失败、超限都会清空。

## 6. 回执局限与执行判据

- **判断这一批是否执行**：`outbox.inbox` 与本批命令一致，且 `executedAt` 不早于投递时刻，才算执行；`outbox` 不存在或 `inbox` 不一致，视为未执行。时间只精确到秒，同秒投递要优先比对 `inbox`。用户确认已刷新仍无回执，提示检查 AI 总开关。
- 单条失败不回滚，前面的命令可能已落盘。
- `ok=true` 不保证数据一定变化，例如取消一个本来未打勾的日期。
- 不保证只执行一次；中途崩溃后下次刷新可能重复执行。
- `snapshot` 是旧时点，用户可能已经改过。

## 7. 失败原因全表

单条命令的 `reason` 只可能是下面这些，按文案判断该改什么。

```text
字段不合格 —— 改字段后重新投递：
缺必填
标题需为 1–12 个字
备注不能超过 500 个字
日期格式应为：YYYY-MM-DD
日期超出支持范围
日期不能早于今天
时间格式应为 HH:MM（24 小时制）
提前提醒需为 0–60 分钟
颜色格式错误
声音不存在
标签名需为 1–6 个字

规则不合格 —— 同上：
重复规则不完整
截止日期格式应为：YYYY-MM-DD
截止日期不能早于起始日期
当前时间已超过日程提醒或提前提醒

目标不合格 —— 重读 snapshot，换目标或告诉用户：
目标不存在
目标已过期
标签不存在
发生日期不存在
不支持的操作

落盘没成功 —— 不要重投，直接告诉用户，多半是文件被占用或数据处于阻断状态：
保存被阻断
文件被占用
保存失败
```

失败与 `ok=true` 都只作用于这一条；同批其他命令的结果各自独立。

## 8. 执行顺序

先读 `snapshot`，用 `id` 找到目标，判断它是否过期、是否重复、挂在哪个标签下；再把用户的话翻译成 `commands`，按第 4 节核对必填、上限与清空语义；写 `inbox` 时整份覆盖，先写 `inbox.tmp` 再改名；然后提示用户点刷新，等回执；读 `outbox` 按每条的 `ok` / `reason` 回复，失败就按第 7 节改完重新投递。
