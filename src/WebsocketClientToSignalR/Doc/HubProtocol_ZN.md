# SignalR Hub协议

SignalR协议是用于在任何基于消息的传输上进行双向RPC的协议。连接中的任何一方都可以调用另一方的过程，并且过程可以返回零个或多个结果或错误。

## 条款

* 呼叫者(Caller)-这是发出的节点`Invocation`，`StreamInvocation`，`CancelInvocation`，`Ping`消息和接收`Completion`，`StreamItem`和`Ping`消息（一个节点既可以是呼叫者和被叫方用于不同调用同时）
* 被叫方(Callee)-接收一个节点`Invocation`，`StreamInvocation`，`CancelInvocation`，`Ping`消息和发布`Completion`，`StreamItem`和`Ping`消息（一个节点既可以是被叫方和主叫方用于不同调用同时）
* 粘合剂(Binder  )-每个节点上的组件，用于处理映射`Invocation`和`StreamInvocation`消息的方法调用和返回值`Completion`和`StreamItem`消息

## 传输要求

SignalR协议要求基础传输具有以下属性。

* 可靠，有序的消息传递-特别是，SignalR协议不提供消息的重新传输或重新排序功能。如果这对于应用程序场景很重要，则应用程序必须使用保证它的传输（即TCP），或者提供自己的系统来管理消息顺序。

## 总览

本文档描述了SignalR协议的两种编码：[JSON](http://www.json.org/)和[MessagePack](http://msgpack.org/)。在连接期间只能使用一种格式，并且在打开连接之后和发送任何其他消息之前，双方都必须同意该格式。但是，每种格式都具有相似的总体结构。

在SignalR协议中，可以发送以下类型的消息：

| 讯息名称            | 发件人         | 描述                                                         |
| ------------------- | -------------- | ------------------------------------------------------------ |
| `HandshakeRequest`  | Client         | 客户发送以同意消息格式。                                     |
| `HandshakeResponse` | Server         | 由服务器发送，作为对先前`HandshakeRequest`消息的确认。如果握手失败，则包含错误。 |
| `Close`             | Callee, Caller | 关闭连接时由服务器发送。如果由于错误而关闭了连接，则包含错误。 |
| `Invocation`        | Caller         | 指示在远程端点上调用带有提供的参数的特定方法（目标）的请求。 |
| `StreamInvocation`  | Caller         | 指示调用带有远程端点上提供的参数的流方法（目标）的请求。     |
| `StreamItem`        | Callee, Caller | 指示来自上`StreamInvocation`一条消息的流响应数据的单个项，或来自具有streamIds的调用的流上传的单个项。 |
| `Completion`        | Callee, Caller | 表示先前`Invocation`或`StreamInvocation`已完成，或中的流`Invocation`或`StreamInvocation`已完成。如果调用以错误结束或非流方法调用的结果结束，则包含错误。`void`方法将不存在结果。在流式调用的情况下，`StreamItem`将不再接收其他消息。 |
| `CancelInvocation`  | Caller         | 客户端发送以取消服务器上的流调用。                           |
| `Ping`              | Caller, Callee | 由任何一方发送，以检查连接是否处于活动状态。                 |

打开与服务器的连接后，客户端必须向服务器发送一条`HandshakeRequest`消息作为其第一条消息。握手消息**始终**是JSON消息，并且包含格式名称（协议）以及在连接期间将使用的协议版本。`HandshakeResponse`如果服务器不支持该协议，服务器将使用且也始终为JSON进行响应，其中包含错误。如果服务器不支持客户端请求的协议，或者从客户端收到的第一条消息不是`HandshakeRequest`消息，则服务器必须关闭连接。无论是`HandshakeRequest`与`HandshakeResponse`消息必须由ASCII字符被终止`0x1E`（记录分隔符）。

该`HandshakeRequest`消息包含以下属性：

* `protocol` - 用于服务器和客户端之间交换消息的协议的名称
* `version` - 对于MessagePack和Json协议，该值必须始终为1

例:

```json
{
    "protocol": "messagepack",
    "version": 1
}
```

该`HandshakeResponse`消息包含以下属性：

* `error` -如果服务器不支持请求的协议，则为可选错误消息

例:

```json
{
    "error": "Requested protocol 'messagepack' is not available."
}
```

## 呼叫者和被呼叫者之间的通信

呼叫者和被呼叫者之间存在三种交互：

* Invocations(调用) - 调用者向被调用者发送一条消息，并期望收到一条消息，表明调用已完成，并且可以选择调用的结果
* Non-Blocking Invocations(非阻塞调用) - 调用方向被调用方发送消息，并且不希望任何其他消息
* Streaming Invocations(流式调用) - 调用方向被调用方发送一条消息，并期望被调用方返回一个或多个结果，然后显示一条消息，指示调用结束

## Invocations

为了执行单个调用，调用者遵循以下基本流程：

1. 分配一个唯一的（每个连接）`Invocation ID`值（由调用者选择的任意字符串）来表示调用
2. 发送`Invocation`或`StreamingInvocation`消息，其中包含`Invocation ID`，`Target`被调用的名称以及`Arguments`提供给方法的。
3. 如果将`Invocation`标记为非阻塞（请参见下面的“非阻塞调用”），请在此处停止并立即返回应用程序。
4. 等待匹配的`StreamItem`或`Completion`消息`Invocation ID`
5. 如果`Completion`收到消息，请转到8
6. 如果`StreamItem`消息具有有效负载，则将有效负载分派给应用程序（即，通过将结果产生给`IObservable`，或在步骤8中收集结果以进行分派）
7. 转到4
8. 完成调用，将最终的有效负载项目（如果有）或错误（如果有）分派给应用程序

一个`Invocation`消息的`Target`必须引用的具体方法，重载是**不**允许的。在.NET Binder中，`Target`方法的值定义为Method的简单名称（即，没有限定类型名称，因为SignalR端点特定于单个Hub类）。`Target`区分大小写

**注意**：`Invocation ID`s是由呼叫者任意选择的，被呼叫者应在所有响应消息中使用相同的字符串。被叫方可以建立合理的`Invocation ID`长度限制，并在`Invocation ID`收到太长的长度时终止连接。

## Message Headers

除`Ping`消息外，所有消息都可以携带其他标头。标头作为带有字符串键和字符串值的字典进行传输。客户端和服务器应忽略不了解的标头。由于在此规范中未定义标头，因此永远不要期望客户端或服务器解释标头。但是，期望客户端和服务器能够处理包含标头的消息，而忽略标头。

## Non-Blocking Invocations

可以发送没有`Invocation ID`值的调用。这表明调用是“非阻塞的”，因此调用者不希望响应。当被调用方收到没有`Invocation ID`值的调用时，它**不得**发送对该调用的任何响应。

## Streaming

SignalR协议允许`StreamItem`响应一条`StreamingInvocation`消息发送多条消息，并允许接收者在这些结果到达时分派这些结果，以允许将数据从一个端点流到另一个端点。

在被调用方，由被调用方的活页夹确定方法调用是否会产生多个结果。例如，在.NET中，某些返回类型可以指示多个结果，而其他返回类型可以指示单个结果。即使这样，应用程序仍可能希望在单个`Completion`帧中缓冲并返回多个结果。由活页夹决定如何映射它。被呼叫者的活页夹必须将每个结果编码为单独的`StreamItem`消息，并通过发送`Completion`消息来指示结果结束。

在调用方，执行调用的用户代码指示如何接收结果，并且调用方的活页夹负责处理结果。如果调用方只期望一个结果，但返回多个结果，或者如果调用方期望有多个结果，但仅返回一个结果，则调用者的活页夹应产生一个错误。如果呼叫者希望在被呼叫者发送`StreamItem`消息之前停止接收`Completion`消息，则呼叫者可以发送`CancelInvocation`与`Invocation ID`用于`StreamInvocation`启动流的消息相同的消息。当被叫方收到`CancelInvocation`消息时，它将停止发送`StreamItem`消息并发送`Completion`消息。呼叫者可以自由忽略任何`StreamItem`消息以及`Completion`发送后的消息`CancelInvocation`。

## Upload streaming

调用方可以将流数据发送给被调用方，他们可以通过创建`Invocation`或`StreamInvocation`并添加带有ID数组的“ StreamIds”属性来开始这样的过程，该ID数组将表示与调用关联的流。这些ID必须与同一调用方使用的任何其他流ID唯一。然后，调用方发送`StreamItem`消息，并将其“ InvocationId”属性设置为它们正在发送的流的ID。呼叫者可以通过发送一条`Completion`消息，告知他们正在完成的流，从而结束流。如果被叫方发送一个`Completion`调用方应停止发送`StreamItem`和`Completion`邮件，并且被叫方可以自由地忽略任何`StreamItem`和`Completion`调用完成后发送邮件。

## Completion and results

仅当`Completion`收到消息时，调用才被视为完成。在接收到针对该调用的消息之后，使用该消息接收**任何**消息都被视为协议错误，并且接收者可以立即终止连接。`Invocation ID``Completion`

如果被调用方要流式传输结果，则**必须**在单独的`StreamItem`消息中发送每个单独的结果，并使用来完成调用`Completion`。如果被叫方是否会返回一个结果，它**必须**不发送任何`StreamItem`消息，并且**必须**在发送一个结果`Completion`消息。如果被调用方收到了`Invocation`将产生多个结果的方法的消息，或者被调用方收到了`StreamInvocation`将返回单个结果的方法的消息，则它**必须**用一条`Completion`包含错误的消息来完成调用。

## Errors

通过消息中`error`字段的存在来指示错误`Completion`。错误始终表示调用即将结束。在流式传输的响应的情况下，到达`Completion`指示错误消息应**不**停止先前接收的结果的调度。仅在分派先前接收的结果之后才产生该错误。

如果任一端点提交了协议错误（请参见下面的示例），则另一端点可能会立即终止基础连接。

- 任何消息缺少必填字段或具有无法识别的字段都是协议错误。
- 呼叫者发送来自被呼叫者的消息中未收到的`StreamItem`或`Completion`消息是协议错误`Invocation ID``Invocation`
- 调用方发送`StreamItem`或`Completion`消息以响应非阻塞调用是协议错误（请参见上面的“非阻塞调用”）
- 如果先前已为同一消息发送了`Completion`消息，则呼叫者发送消息并带有结果是协议错误。`StreamItem``Invocation ID`
- 呼叫者发送`Completion`带有结果和错误的消息是协议错误。
- `Invocation`或`StreamInvocation`消息`Invocation ID`具有已被*该*端点使用的消息是协议错误。但是，一个端点使用另一个端点先前使用的**并不是错误**`Invocation ID`（允许每个端点跟踪其自己的ID）。

## Ping (aka "Keep Alive")

SignalR集线器协议支持“保持活动”消息，该消息用于确保基础传输连接保持活动状态。这些消息有助于确保：

1. 代理在空闲时间（发送少量消息时）不会关闭基础连接
2. 如果在没有适当终止的情况下删除了基础连接，则会尽快通知应用程序。

保持活动状态是通过`Ping`消息类型实现的。**任一端点**均可`Ping`随时发送消息。接收端点可以选择忽略该消息，无论如何它没有义务进行响应。大多数实现都希望重置用于确定对方是否存在的超时。

Ping消息没有任何有效负载，它们是完全空的消息（除了将消息标识为消息所必需的编码之外`Ping`）。

默认的ASP.NET Core实现会自动ping通活动连接上的两个方向。这些ping是定期进行的，并允许检测到意外断开连接（例如，拔下服务器）。如果客户端检测到服务器已停止ping，则客户端将关闭连接，反之亦然。如果通过该连接还有其他流量，则不需要保持连接ping。`Ping`仅当间隔过去了而没有发送消息时才发送A。

## Example

考虑以下C＃方法

```csharp
public int Add(int x, int y)
{
    return x + y;
}

public int SingleResultFailure(int x, int y)
{
    throw new Exception("It didn't work!");
}

public IEnumerable<int> Batched(int count)
{
    for (var i = 0; i < count; i++)
    {
        yield return i;
    }
}

public async IAsyncEnumerable<int> Stream(int count)
{
    for (var i = 0; i < count; i++)
    {
        await Task.Delay(10);
        yield return i;
    }
}

public async IAsyncEnumerable<int> StreamFailure(int count)
{
    for (var i = 0; i < count; i++)
    {
        await Task.Delay(10);
        yield return i;
    }
    throw new Exception("Ran out of data!");
}

private List<string> _callers = new List<string>();
public void NonBlocking(string caller)
{
    _callers.Add(caller);
}

public async Task<int> AddStream(IAsyncEnumerable<int> stream)
{
    int sum = 0;
    await foreach(var item in stream)
    {
        sum += item;
    }
    return sum;
}
```

在下面的每个示例中，开头的行`C->S`指示从呼叫者（“客户端”）发送给被呼叫者（“服务器”）的消息，行的开头指示从被呼叫者（“服务器”）`S->C`发送回呼叫者（“客户端”）的消息”）。消息语法只是一个伪代码，并不旨在匹配任何特定的编码。

### 单一结果 (`Add` example above)

```
C->S: Invocation { Id = 42, Target = "Add", Arguments = [ 40, 2 ] }
S->C: Completion { Id = 42, Result = 42 }
```

**注意：**以下**不是**此调用的可接受的编码：

```
C->S: Invocation { Id = 42, Target = "Add", Arguments = [ 40, 2 ] }
S->C: StreamItem { Id = 42, Item = 42 }
S->C: Completion { Id = 42 }
```

### 单个结果有错误 (`SingleResultFailure` example above)

```
C->S: Invocation { Id = 42, Target = "SingleResultFailure", Arguments = [ 40, 2 ] }
S->C: Completion { Id = 42, Error = "It didn't work!" }
```

### 批处理结果 (`Batched` example above)

```
C->S: Invocation { Id = 42, Target = "Batched", Arguments = [ 5 ] }
S->C: Completion { Id = 42, Result = [ 0, 1, 2, 3, 4 ] }
```

### 流式结果 (`Stream` example above)

```
C->S: StreamInvocation { Id = 42, Target = "Stream", Arguments = [ 5 ] }
S->C: StreamItem { Id = 42, Item = 0 }
S->C: StreamItem { Id = 42, Item = 1 }
S->C: StreamItem { Id = 42, Item = 2 }
S->C: StreamItem { Id = 42, Item = 3 }
S->C: StreamItem { Id = 42, Item = 4 }
S->C: Completion { Id = 42 }
```

**注意：**以下**不是**此调用的可接受的编码：

```
C->S: StreamInvocation { Id = 42, Target = "Stream", Arguments = [ 5 ] }
S->C: StreamItem { Id = 42, Item = 0 }
S->C: StreamItem { Id = 42, Item = 1 }
S->C: StreamItem { Id = 42, Item = 2 }
S->C: StreamItem { Id = 42, Item = 3 }
S->C: Completion { Id = 42, Result = 4 }
```

这是无效的，因为`Completion`用于流调用的消息不得包含任何结果。

### 流式传输结果有错误 (`StreamFailure` example above)

```
C->S: StreamInvocation { Id = 42, Target = "Stream", Arguments = [ 5 ] }
S->C: StreamItem { Id = 42, Item = 0 }
S->C: StreamItem { Id = 42, Item = 1 }
S->C: StreamItem { Id = 42, Item = 2 }
S->C: StreamItem { Id = 42, Item = 3 }
S->C: StreamItem { Id = 42, Item = 4 }
S->C: Completion { Id = 42, Error = "Ran out of data!" }
```

这应该体现到调用代码作为序列发射`0`，`1`，`2`，`3`，`4`，但随后失败，出现错误`Ran out of data!`。

### 流式结果提前关闭 (`Stream` example above)

```
C->S: StreamInvocation { Id = 42, Target = "Stream", Arguments = [ 5 ] }
S->C: StreamItem { Id = 42, Item = 0 }
S->C: StreamItem { Id = 42, Item = 1 }
C->S: CancelInvocation { Id = 42 }
S->C: StreamItem { Id = 42, Item = 2} // This can be ignored
S->C: Completion { Id = 42 } // This can be ignored
```

### 非阻塞呼叫 (`NonBlocking` example above)

```
C->S: Invocation { Target = "NonBlocking", Arguments = [ "foo" ] }
```

### 从客户端流到服务器 (`AddStream` example above)

```
C->S: Invocation { Id = 42, Target = "AddStream", Arguments = [ ], StreamIds = [ 1 ] }
C->S: StreamItem { Id = 1, Item = 1 }
C->S: StreamItem { Id = 1, Item = 2 }
C->S: StreamItem { Id = 1, Item = 3 }
C->S: Completion { Id = 1 }
S->C: Completion { Id = 42, Result = 6 }
```

### Ping

```
C->S: Ping
```

## JSON Encoding

在SignalR协议的JSON编码中，每个消息都表示为一个JSON对象，它应该是来自传输的基础消息的唯一内容。所有属性名称均区分大小写。底层协议应该处理文本的编码和解码，因此JSON字符串应以底层传输期望的任何形式进行编码。例如，当使用ASP.NET套接字传输时，UTF-8编码始终用于文本。

所有JSON消息都必须以ASCII字符`0x1E`（记录分隔符）终止。

### 调用消息编码

一个`Invocation`消息是具有以下性质的JSON对象：

- `type`-`Number`文字值为1的A ，表示此消息是调用。
- `invocationId`-消息的可选`String`编码`Invocation ID`。
- `target`-`String`编码`Target`名称，如被调用方的活页夹所期望的
- `arguments`-`Array`包含应用于Target中引用的方法的参数。这是JSON`Token`的序列，按照下面的“ JSON有效负载编码”部分中的指示进行编码
- `streamIds`-可选`Array`的字符串，代表从调用者到被调用者的流的唯一ID，并由Target中引用的方法使用。

Example:

```json
{
    "type": 1,
    "invocationId": "123",
    "target": "Send",
    "arguments": [
        42,
        "Test Message"
    ]
}
```
Example (Non-Blocking):

```json
{
    "type": 1,
    "target": "Send",
    "arguments": [
        42,
        "Test Message"
    ]
}
```

Example (Invocation with stream from Caller):

```json
{
    "type": 1,
    "invocationId": "123",
    "target": "Send",
    "arguments": [
        42
    ],
    "streamIds": [
        "1"
    ]
}
```

### StreamInvocation消息编码

`StreamInvocation`消息是具有以下性质的JSON对象：

- `type`-`Number`具有文字值4的A ，指示此消息是StreamInvocation。
- `invocationId`-消息的`String`编码`Invocation ID`。
- `target`-`String`编码`Target`名称，如被调用方的活页夹所期望的那样。
- `arguments`-`Array`包含应用于Target中引用的方法的参数。这是JSON`Token`的序列，按照下面的“ JSON有效负载编码”部分中的指示进行编码。
- `streamIds`-可选`Array`的字符串，代表从调用者到被调用者的流的唯一ID，并由Target中引用的方法使用。

Example:

```json
{
    "type": 4,
    "invocationId": "123",
    "target": "Send",
    "arguments": [
        42,
        "Test Message"
    ]
}
```

### StreamItem 消息编码

 `StreamItem` 消息是具有以下性质的JSON对象：

* `type`-`Number`字面值为2的A ，表示此消息为`StreamItem`。
* `invocationId`-消息的`String`编码`Invocation ID`。
* `item`-`Token`对流项目进行编码（有关详细信息，请参见“ JSON有效负载编码”）。

Example

```json
{
    "type": 2,
    "invocationId": "123",
    "item": 42
}
```

### Completion Message Encoding

 `Completion` 消息是具有以下性质的JSON对象

* `type`-`Number`具有文字值的A `3`，表示此消息是`Completion`。
* `invocationId`-消息的`String`编码`Invocation ID`。
* `result`-`Token`对结果值进行编码（有关详细信息，请参见“ JSON有效负载编码”）。如果存在，则**忽略**此字段`error`。
* `error`-`String`编码错误消息。

在消息中同时包含`result`和`error`属性是协议错误`Completion`。合格端点可以在接收到此类消息后立即终止连接。

示例-`Completion`没有结果或错误的消息

```json
{
    "type": 3,
    "invocationId": "123"
}
```

示例-`Completion`一条带有结果的消息

```json
{
    "type": 3,
    "invocationId": "123",
    "result": 42
}
```

示例-`Completion`错误消息

```json
{
    "type": 3,
    "invocationId": "123",
    "error": "It didn't work!"
}
```

示例-以下`Completion`消息是协议错误，因为它同时具有`result`和`error`

```json
{
    "type": 3,
    "invocationId": "123",
    "result": 42,
    "error": "It didn't work!"
}
```

### CancelInvocation Message Encoding
A `CancelInvocation` 消息是具有以下性质的JSON对象

* `type`-`Number`具有文字值的 `5`，表示此消息是`CancelInvocation`。
* `invocationId`-消息的`String`编码`Invocation ID`。

Example
```json
{
    "type": 5,
    "invocationId": "123"
}
```

### Ping Message Encoding
 `Ping` 消息是具有以下性质的JSON对象：

* `type`-`Number`具有文字值的A `6`，表示此消息是`Ping`。

Example
```json
{
    "type": 6
}
```

### Close Message Encoding
`Close` 消息是具有以下性质的JSON对象

* `type`-`Number`具有文字值的A `7`，表示此消息是`Close`。
* `error`-可选的`String`编码错误消息。
* `allowReconnect`-可选选项`Boolean`，用于向启用了自动重新连接的客户端指示，他们应在收到消息后尝试重新连接。

示例-`Close`一条没有错误的消息
```json
{
    "type": 7
}
```

示例-`Close`错误消息
```json
{
    "type": 7,
    "error": "Connection closed because of an error!"
}
```

示例-`Close`带有错误的消息，该错误允许客户端自动重新连接。
```json
{
    "type": 7,
    "error": "Connection closed because of an error!",
    "allowReconnect": true
}
```

### JSON Header Encoding

消息头被编码为带有字符串值的JSON对象，该字符串值存储在`headers`属性中。

例如：

```json
{
    "type": 1,
    "headers": {
        "Foo": "Bar"
    },
    "invocationId": "123",
    "target": "Send",
    "arguments": [
        42,
        "Test Message"
    ]
}
```


### JSON Payload Encoding

该内的参数数组中的项`Invocation`的消息类型，以及所述`item`的价值`StreamItem`信息和`result`所述价值`Completion`信息，这意味着已经对每个特定的粘合剂编码值。本文档结尾的“类型映射”部分提供了对这些值进行编码/解码的一般准则，但是Binders应该为应用程序提供配置，以允许他们自定义这些映射。这些映射不必是自描述的，因为在解码该值时，预计活页夹将知道目标类型（通过查找Target指示的方法的定义）。

## MessagePack (MsgPack) encoding

在SignalR协议的MsgPack编码中，每个消息都表示为单个MsgPack数组，其中包含与给定集线器协议消息的属性相对应的项。数组项可以是原始值，数组（例如方法参数）或对象（例如参数值）。数组中的第一项是消息类型。

MessagePack使用不同的格式来编码值。有关格式定义，请参阅[MsgPack格式规范](https://github.com/msgpack/msgpack/blob/master/spec.md#formats)。

### Invocation Message Encoding

`Invocation` 消息具有以下结构：

```
[1, Headers, InvocationId, Target, [Arguments], [StreamIds]]
```

- `1`-消息类型-`1`表示这是一条`Invocation`消息。
- `Headers` -包含标题的MsgPack映射，以及字符串键和字符串值（请参见下面的MessagePack标题编码）
- InvocationId-以下之一：
  - A `Nil`，表示没有调用ID，或者
  - `String`消息的调用ID的编码。
- 目标-`String`编码目标名称的名称，如被调用方的活页夹所期望的那样。
- Arguments-一个数组，其中包含要应用于Target中引用的方法的参数。
- StreamIds-`Array`字符串的一个，代表从调用方到被调用方的流的唯一ID，并由Target中引用的方法使用。

#### Example:

以下有效payload

```
0x96 0x01 0x80 0xa3 0x78 0x79 0x7a 0xa6 0x6d 0x65 0x74 0x68 0x6f 0x64 0x91 0x2a 0x90
```

解码如下：

* `0x96` -6元素数组
* `0x01`- `1`（消息类型-`Invocation`消息）
* `0x80` -长度为0的地图（标题）
* `0xa3` -长度为3（InvocationId）的字符串
* `0x78` -- `x`
* `0x79` -- `y`
* `0x7a` -- `z`
* `0xa6` -长度为6的字符串（目标）
* `0x6d` -- `m`
* `0x65` -- `e`
* `0x74` -- `t`
* `0x68` -- `h`
* `0x6f` -- `o`
* `0x64` -- `d`
* `0x91` -1元素数组（参数）
* `0x2a`- `42`（参数值）
* `0x90` -0元素数组（StreamIds）

#### Non-Blocking Example:

以下有效payload
```
0x96 0x01 0x80 0xc0 0xa6 0x6d 0x65 0x74 0x68 0x6f 0x64 0x91 0x2a 0x90
```

解码如下：

* `0x96` -6元素数组
* `0x01`- `1`（消息类型-`Invocation`消息）
* `0x80` -长度为0的地图（标题）
* `0xc0`- `nil`（调用ID）
* `0xa6` -长度为6的字符串（目标）
* `0x6d` -- `m`
* `0x65` -- `e`
* `0x74` -- `t`
* `0x68` -- `h`
* `0x6f` -- `o`
* `0x64` -- `d`
* `0x91` -1元素数组（参数）
* `0x2a`- `42`（参数值）
* `0x90` -0元素数组（StreamIds）

### StreamInvocation Message Encoding

`StreamInvocation` 消息具有以下结构：

```
[4, Headers, InvocationId, Target, [Arguments], [StreamIds]]
```

* `4`-消息类型-`4`表示这是一条`StreamInvocation`消息。
* `Headers` -包含标题的MsgPack映射，以及字符串键和字符串值（请参见下面的MessagePack标题编码）
* InvocationId-`String`消息的调用ID的编码。
* 目标-`String`编码目标名称的名称，如被调用方的活页夹所期望的那样。
* Arguments-一个数组，其中包含要应用于Target中引用的方法的参数。
* StreamIds-`Array`字符串的一个，代表从调用方到被调用方的流的唯一ID，并由Target中引用的方法使用。

例：

以下有效 payload

```
0x96 0x04 0x80 0xa3 0x78 0x79 0x7a 0xa6 0x6d 0x65 0x74 0x68 0x6f 0x64 0x91 0x2a 0x90
```

解码如下：

* `0x96` -6元素数组
* `0x04`- `4`（消息类型-`StreamInvocation`消息）
* `0x80` -长度为0的地图（标题）
* `0xa3` -长度为3（InvocationId）的字符串
* `0x78` -- `x`
* `0x79` -- `y`
* `0x7a` -- `z`
* `0xa6` -长度为6的字符串（目标）
* `0x6d` -- `m`
* `0x65` -- `e`
* `0x74` -- `t`
* `0x68` -- `h`
* `0x6f` -- `o`
* `0x64` -- `d`
* `0x91` -1元素数组（参数）
* `0x2a`- `42`（参数值）
* `0x90` -0元素数组（StreamIds）

### StreamItem Message Encoding

`StreamItem` 消息具有以下结构：

```
[2, Headers, InvocationId, Item]
```

* `2`-消息类型-`2`表示这是一条`StreamItem`消息
* `Headers` -包含标题的MsgPack映射，以及字符串键和字符串值（请参见下面的MessagePack标题编码）
* InvocationId-`String`消息的调用ID的编码
* Item-流项目的值

例：

以下有效 payload:

```
0x94 0x02 0x80 0xa3 0x78 0x79 0x7a 0x2a
```

解码如下：

* `0x94` -4元素数组
* `0x02`- `2`（消息类型-`StreamItem`消息）
* `0x80` -长度为0的地图（标题）
* `0xa3` -长度为3（InvocationId）的字符串
* `0x78` -- `x`
* `0x79` -- `y`
* `0x7a` -- `z`
* `0x2a`- `42` (Item)

### Completion Message Encoding

`Completion` 消息具有以下结构

```
[3, Headers, InvocationId, ResultKind, Result?]
```

* `3`-消息类型-`3`表示这是一条`Completion`消息
* `Headers` -包含标题的MsgPack映射，以及字符串键和字符串值（请参见下面的MessagePack标题编码）
* InvocationId-`String`消息的调用ID的编码
* ResultKind-指示调用结果类型的标志：
    - `1`-错误结果-结果包含`String`带有错误消息的
    - `2` -结果无效-结果不存在
    - `3` -非无效结果-结果包含服务器返回的值
* 结果-一个可选项目，包含调用结果。如果服务器未返回任何值，则为空（无效方法）

例子：

#### 错误结果：

以下有效payload:

```
0x95 0x03 0x80 0xa3 0x78 0x79 0x7a 0x01 0xa5 0x45 0x72 0x72 0x6f 0x72
```

解码如下:

* `0x94` -4元素数组
* `0x03`- `3`（消息类型-`Result`消息）
* `0x80` -长度为0的地图（标题）
* `0xa3` -长度为3（InvocationId）的字符串
* `0x78` -- `x`
* `0x79` -- `y`
* `0x7a` -- `z`
* `0x01`- `1`（ResultKind-错误结果）
* `0xa5` -长度为5的字符串
* `0x45` -- `E`
* `0x72` -- `r`
* `0x72` -- `r`
* `0x6f` -- `o`
* `0x72` -- `r`

#### Void 结果：

以下有效payload:

```
0x94 0x03 0x80 0xa3 0x78 0x79 0x7a 0x02
```

is decoded as follows:

* `0x94` -4元素数组
* `0x03`- `3`（消息类型-`Result`消息）
* `0x80` -长度为0的地图（标题）
* `0xa3` -长度为3（InvocationId）的字符串
* `0x78` -- `x`
* `0x79` -- `y`
* `0x7a` -- `z`
* `0x02`- `2`(ResultKind - Void result)

#### Non-Void 结果：

以下有效 payload:

```
0x95 0x03 0x80 0xa3 0x78 0x79 0x7a 0x03 0x2a
```

解码如下：

* `0x95` -五元素数组
* `0x03`- `3`（消息类型-`Result`消息）
* `0x80` -长度为0的地图（标题）
* `0xa3` -长度为3（InvocationId）的字符串
* `0x78` -- `x`
* `0x79` -- `y`
* `0x7a` -- `z`
* `0x03`- `3`（ResultKind-非无效结果）
* `0x2a`- `42`（结果）

### CancelInvocation Message Encoding

`CancelInvocation` 消息具有以下结构

```
[5, Headers, InvocationId]
```

* `5`-消息类型-`5`表示这是一条`CancelInvocation`消息
* `Headers` -包含标题的MsgPack映射，以及字符串键和字符串值（请参见下面的MessagePack标题编码）
* InvocationId-`String`消息的调用ID的编码

例：

以下有效payload:

```
0x93 0x05 0x80 0xa3 0x78 0x79 0x7a
```

解码如下：

* `0x93` -三元素数组
* `0x05`- `5`（消息类型`CancelInvocation`消息）
* `0x80` -长度为0的地图（标题）
* `0xa3` -长度为3（InvocationId）的字符串
* `0x78` -- `x`
* `0x79` -- `y`
* `0x7a` -- `z`

### Ping Message Encoding

`Ping` 消息具有以下结构

```
[6]
```

* `6`-消息类型-`6`表示这是一条`Ping`消息。

例子：

#### ping信息

以下有效 payload:

```
0x91 0x06
```

* 解码如下：
  - `0x91` -1元素数组
  - `0x06`- `6`（消息类型-`Ping`消息）

### Close Message Encoding

`Close` 消息具有以下结构

```
[7, Error, AllowReconnect?]
```

* `7`-消息类型-`7`表示这是一条`Close`消息。
* `Error`-错误-`String`编码消息错误的代码。
* `AllowReconnect`-可选选项`Boolean`，用于向启用了自动重新连接的客户端指示，他们应在收到消息后尝试重新连接。

例子：

#### Close message

以下有效payload:
```
0x92 0x07 0xa3 0x78 0x79 0x7a
```

解码如下：

- `0x92` -2元素数组
- `0x07`- `7`（消息类型-`Close`消息）
- `0xa3` -长度为3的字符串（错误）
- `0x78` -- `x`
- `0x79` -- `y`
- `0x7a` -- `z`

#### 关闭消息，允许客户端自动重新连接

以下有效 payload:
```
0x93 0x07 0xa3 0x78 0x79 0x7a 0xc3
```

* 解码如下：
  - `0x93` -三元素数组
  - `0x07`- `7`（消息类型-`Close`消息）
  - `0xa3` -长度为3的字符串（错误）
  - `0x78` -- `x`
  - `0x79` -- `y`
  - `0x7a` -- `z`
  - `0xc3`- `True`（允许重新连接）

### MessagePack Headers Encoding

标头在MessagePack消息中编码为紧随类型值之后的Map。Map可以为空，在这种情况下，它由byte表示`0x80`。如果地图中有项目，则键和值都必须是String值。

标头在Ping消息中无效。Ping消息**始终正确编码**为`0x91 0x06`

下面显示了包含标头的消息的示例编码：

```
0x96 0x01 0x82 0xa1 0x78 0xa1 0x79 0xa1 0x7a 0xa1 0x7a 0xa3 0x78 0x79 0x7a 0xa6 0x6d 0x65 0x74 0x68 0x6f 0x64 0x91 0x2a 0x90
```

并解码如下：

- `0x96` -6元素数组
- `0x01`- `1`（消息类型-`Invocation`消息）
- `0x82` -长度为2的地图
- `0xa1` -长度为1的字符串（键）
- `0x78` -- `x`
- `0xa1` -长度为1的字符串（值）
- `0x79` -- `y`
- `0xa1` -长度为1的字符串（键）
- `0x7a` -- `z`
- `0xa1` -长度为1的字符串（值）
- `0x7a` -- `z`
- `0xa3` -长度为3（InvocationId）的字符串
- `0x78` -- `x`
- `0x79` -- `y`
- `0x7a` -- `z`
- `0xa6` -长度为6的字符串（目标）
- `0x6d` -- `m`
- `0x65` -- `e`
- `0x74` -- `t`
- `0x68` -- `h`
- `0x6f` -- `o`
- `0x64` -- `d`
- `0x91` -1元素数组（参数）
- `0x2a`- `42`（参数值）
- `0x90` -0元素数组（StreamIds）

并解释为带有标题的调用消息：`'x' = 'y'`和`'z' = 'z'`。

## 类型映射

以下是JSON类型与.NET客户端之间的一些示例类型映射。这不是详尽无遗或权威性的清单，而只是提供指导。官方客户端将为用户提供方法，以覆盖特定方法，参数或参数类型的默认映射行为

| .NET类型                                        | JSON类型                | MsgPack格式系列            |
| ----------------------------------------------- | ----------------------- | -------------------------- |
| `System.Byte`，`System.UInt16`，`System.UInt32` | `Number`                | `positive fixint`， `uint` |
| `System.SByte`，`System.Int16`，`System.Int32`  | `Number`                | `fixint`， `int`           |
| `System.UInt64`                                 | `Number`                | `positive fixint`， `uint` |
| `System.Int64`                                  | `Number`                | `fixint`， `int`           |
| `System.Single`                                 | `Number`                | `float`                    |
| `System.Double`                                 | `Number`                | `float`                    |
| `System.Boolean`                                | `true` 要么 `false`     | `true`， `false`           |
| `System.String`                                 | `String`                | `fixstr`， `str`           |
| `System.Byte`[]                                 | `String` （Base64编码） | `bin`                      |
| `IEnumerable<T>`                                | `Array`                 | `bin`                      |
| 习俗 `enum`                                     | `Number`                | `fixint`， `int`           |
| 自定义`struct`或`class`                         | `Object`                | `fixmap`， `map`           |

MessagePack有效载荷包装在下面描述的外部消息框架中。

#### 二进制编码

```
([Length][Body])([Length][Body])... continues until end of the connection ...
```

* `[Length]`-编码为VarInt的32位无符号整数。可变大小-1-5个字节。
* `[Body]`-消息的正文，`[Length]`长度精确为字节。

##### VarInt

VarInt将最高有效位编码为标记，指示该字节是VarInt的最后一个字节还是跨度到下一个字节。字节以相反的顺序出现-即第一个字节包含该值的最低有效位。

例子：

- VarInt：`0x35`（`%00110101`）-最高有效位为0，因此值为％x0110101，即0x35（53）
- VarInt：`0x80 0x29`（`%10000000 %00101001`）-第一个字节的最高有效位是1，因此其余位（％x0000000）是该值的最低位。第二个字节的最高有效位为0，表示这是VarInt的最后一个字节。实际值位（％x0101001）需要放在我们已经读取的位之前，因此值是％01010010000000，即0x1480（5248）

支持的最大有效负载大小为2GB，因此我们需要支持的最大数量为0x7fffffff，当编码为VarInt时为0xFF 0xFF 0xFF 0xFF 0x07-因此，长度前缀的最大大小为5个字节。

例如，在发送以下帧时（`\n`指示实际的换行字符，而不是转义序列）：

- “ Hello \ nWorld”
- `0x01 0x02`

编码将如下所示，为十六进制的二进制数字列表（括号`()`中的文本为注释）。空格和换行符无关，仅用于说明。

```
0x0B                                                   (start of frame; VarInt value: 11)
0x68 0x65 0x6C 0x6C 0x6F 0x0A 0x77 0x6F 0x72 0x6C 0x64 (UTF-8 encoding of 'Hello\nWorld')
0x02                                                   (start of frame; VarInt value: 2)
0x01 0x02                                              (body)
```
