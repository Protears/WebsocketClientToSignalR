# 传输协议

本文档描述了三种ASP.NET端点传输所使用的协议：WebSocket，服务器发送的事件和长轮询

## 运输要求

传输必须具有以下属性：

1. 双工-能够从服务器到客户端以及从客户端到服务器发送消息
2. 二进制安全-不论内容如何，都可以传输任意二进制数据
3. 文本安全-能够传输任意文本数据，并保留内容。行尾必须保留，**但可以转换为其他格式**。例如`\r\n`可以转换为`\n`。这是由于某些传输方式（服务器已发送事件）中的怪癖所致。如果需要保留确切的行尾，则应将数据作为`Binary`消息发送。

完全满足双工要求的唯一传输是WebSocket，其他是实现半工连接一端的“半传输”。它们结合使用以实现双工连接。

在整个文档中，该术语`[endpoint-base]`用于指代分配给特定终点的路由。术语`connection-id`和`connectionToken`用于指代`POST [endpoint-base]/negotiate`请求提供的连接ID和连接令牌。

**关于错误的说明：**在所有错误情况下，默认情况下，**从不**提供详细的异常消息。可以提供简短的描述字符串。但是，应用程序开发人员可以选择允许发出详细的异常消息，该消息仅应在`Development`环境中使用。意外错误通过HTTP`500 Server Error`状态代码或WebSockets非`1000 Normal Closure`封闭框架传达；在这种情况下，应将连接视为终止。

## `POST [endpoint-base]/negotiate` 请求

该`POST [endpoint-base]/negotiate`请求用于在客户端和服务器之间建立连接。

在POST请求中，客户端发送一个查询字符串参数，其中包含键“ negotiateVersion”和该值作为要使用的协商协议版本。如果省略查询字符串，则服务器会将版本视为零。服务器将在json响应中包含一个“ negotiateVersion”属性，该属性指示它将使用哪个版本。选择的版本如下所述：

- 如果服务器支持的最低协议版本大于客户端请求的版本，它将发送错误响应并关闭连接
- 如果服务器支持请求版本，它将以请求的版本响应
- 如果请求的版本大于服务器最大支持的版本，则服务器将以其最大支持的版本进行响应如果响应中的“ negotiateVersion”不可接受，则客户端可能会关闭连接。

响应的内容类型是`application/json`且是JSON负载，其中包含可帮助客户端建立持久连接的属性。客户端不知道的额外JSON属性应被忽略。这允许将来添加新文件而不会破坏较旧的客户。

### 版本1

当服务器和客户端在版本1上达成一致时，服务器响应除了“ connectionId”属性外还将包括“ connectionToken”属性。“ connectionToken”属性的值将在以下所述的HTTP请求的“ id”查询字符串中使用，此值应保密。

成功的协商响应将类似于以下负载：

```
{
   “ connectionToken ”：“ 05265228-1e2c-46c5-82a1-6a5bcc3f0143 ”，
   “ connectionId ”：“ 807809a5-31bf-470d-9e23-afaee35d8a0d ”，
   “ negotiationVersion ”：1，
   “ availableTransports ”：[
    {
      “ transport ”： “ WebSockets ”，
       “ transferFormats ”：[ “ Text ”， “ Binary ” ]
    }，
    {
      “ transport ”： “ ServerSentEvents ”，
       “ transferFormats ”：[ “文本” ]
    }，
    {
      “ transport ”： “ LongPolling ”，
       “ transferFormats ”：[ “ Text ”， “ Binary ” ]
    }
  ]
}
```

从此端点返回的有效负载提供以下数据：

- 在`connectionToken`其**需要**由长轮询和服务器发送的事件传输（以关联发送和接收）。
- ，`connectionId`这是其他客户端可以引用的ID。
- 在`negotiateVersion`这是协商协议版本被服务器和客户机之间使用。
- 在`availableTransports`描述了传输服务器支持列表。对于每个传输，传输（名称`transport`）列，因为是由运输支持“传输格式”的列表（`transferFormats`）

### 版本0

当服务器和客户端在版本0上达成一致时，服务器响应将包括“ connectionId”属性，该属性在“ id”查询字符串中用于以下所述的HTTP请求。

成功的协商响应将类似于以下负载：

```
{
   “ connectionId ”：“ 807809a5-31bf-470d-9e23-afaee35d8a0d ”，
   “ negotiationVersion ”：0，
   “ availableTransports ”：[
    {
      “ transport ”： “ WebSockets ”，
       “ transferFormats ”：[ “ Text ”， “ Binary ” ]
    }，
    {
      “ transport ”： “ ServerSentEvents ”，
       “ transferFormats ”：[ “文本” ]
    }，
    {
      “ transport ”： “ LongPolling ”，
       “ transferFormats ”：[ “ Text ”， “ Binary ” ]
    }
  ]
}
```

从此端点返回的有效负载提供以下数据：

- 在`connectionId`其**需要**由长轮询和服务器发送的事件传输（以关联发送和接收）。
- 在`negotiateVersion`这是协商协议版本被服务器和客户机之间使用。
- 在`availableTransports`描述了传输服务器支持列表。对于每个传输，传输（名称`transport`）列，因为是由运输支持“传输格式”的列表（`transferFormats`）

### 所有版本

还有其他两种可能的协商响应：

1. 重定向响应，告诉客户端结果使用哪个URL和可选的访问令牌。

```
{
   “ url ”：“ https://myapp.com/chat ”，
   “ accessToken ”：“ accessToken ” 
}
```

从此端点返回的有效负载提供以下数据：

- 该`url`这是客户端需要连接到的URL。
- ，`accessToken`这是用于访问指定网址的可选承载令牌。

1. 包含的响应`error`应停止连接尝试。

```
{
   “ error ”：“不允许此连接。” 
}
```

从此端点返回的有效负载提供以下数据：

- 将`error`给出关于为什么洽谈失败的细节。

## 传输格式

ASP.NET端点支持两种不同的传输格式：`Text`和`Binary`。`Text`引用UTF-8文本，并`Binary`引用任何任意二进制数据。传输格式有两个目的。首先，在WebSockets传输中，它用于确定是否应使用WebSocket框架`Text`或`Binary`WebSocket框架来承载数据。这在调试中很有用，因为大多数浏览器开发工具仅显示`Text`框架的内容。当使用基于文本的协议（如JSON）时，WebSockets传输最好使用`Text`框架。客户端/服务器如何指示当前使用的传输格式是实现定义的。

某些传输仅限于仅支持`Text`数据（特别是服务器发送的事件）。由于它们的协议限制，这些传输不能携带任意二进制数据（没有其他编码，例如Base-64）。每个传输支持的传输格式被描述为`POST [endpoint-base]/negotiate`响应的一部分，以允许客户端在需要发送/接收数据时忽略不能支持任意二进制数据的传输。客户端如何指示其希望使用的传输格式也是实现定义的。

## WebSockets（全双工）

WebSockets传输是唯一的，因为它是全双工的，并且可以在单个操作中建立持久连接。结果，不需要客户端使用该`POST [endpoint-base]/negotiate`请求来预先建立连接。它还在其自己的帧元数据中包含所有必需的元数据。

通过建立与WebSocket的连接来激活WebSocket传输`[endpoint-base]`。该**可选的** `id`查询字符串值被用来识别附加到连接。如果没有`id`查询字符串值，则建立新连接。如果指定了参数，但没有与指定ID值的连接，`404 Not Found`则返回响应。收到此请求后，将建立连接，并且服务器将`101 Switching Protocols`立即通过WebSocket升级（）进行响应，以准备发送/接收帧。WebSocket OpCode字段用于指示帧的类型（文本或二进制）。

如果已经存在与Endpoints连接关联的WebSocket连接，则不允许建立第二个WebSocket连接，否则将失败，并显示`409 Conflict`状态代码。

建立连接时的错误通过返回`500 Server Error`状态码作为对升级请求的响应来处理。这包括初始化EndPoint类型的错误。未处理的应用程序错误会触发WebSocket`Close`框架，其原因码与规范中的错误相匹配（对于错误消息，例如消息太大或无效的UTF-8）。对于连接过程中的其他意外错误，将使用非`1000 Normal Closure`状态代码。

## HTTP发布（仅客户端到服务器）

HTTP Post是一种半传输，它只能将消息从客户端发送到服务器，因此，它**始终**与可以从服务器发送到客户端的其他半传输之一一起使用（服务器发送事件和长轮询） ）。

此传输要求使用该`POST [endpoint-base]/negotiate`请求建立连接。

向URL发出HTTP POST请求`[endpoint-base]`。该**强制** `id`查询字符串值被用来识别发送到连接。如果没有`id`查询字符串值，`400 Bad Request`则返回响应。收到**整个**有效负载后，服务器将处理该有效负载并响应`200 OK`是否成功处理了该有效负载。如果`/`在现有请求尚未解决的情况下客户端提出另一个请求，则服务器将立即使用`409 Conflict`状态代码终止新请求。

如果客户端收到`409 Conflict`请求，则连接保持打开状态。任何其他响应都表明由于错误而终止了连接。

如果相关连接已终止，`404 Not Found`则返回状态码。如果实例化EndPoint或调度消息时发生错误，`500 Server Error`则返回状态码。

## 服务器发送的事件（仅服务器到客户端）

服务器发送事件（SSE）是WHATWG在https://html.spec.whatwg.org/multipage/comms.html#server-sent-events指定的协议。它只能将数据从服务器发送到客户端，因此必须与HTTP Post传输配对。它还要求已经使用该`POST [endpoint-base]/negotiate`请求建立了连接。

该协议类似于长轮询，因为客户端向端点打开一个请求并将其保持打开状态。服务器使用SSE协议将帧作为“事件”发送。该协议将单个事件编码为键值对行的序列，并由`:`或使用分隔`\r\n`，`\n`或`\r`作为行终止符，后跟最后的空行。键可以重复，其值与串联`\n`。因此，以下代表两个事件：

```
foo: bar
baz: boz
baz: biz
quz: qoz
baz: flarg

foo: boz
```

在第一种情况下，的值`baz`将是`boz\nbiz\nflarg`，由于上述的级联行为。完整的细节可以在上面链接的规范中找到。

在这种传输中，客户端建立一个SSE连接到`[endpoint-base]`用`Accept`的报头`text/event-stream`，并与用HTTP响应服务器响应`Content-Type`的`text/event-stream`。该**强制** `id`查询字符串值被用来识别发送到连接。如果没有`id`查询字符串值，`400 Bad Request`则返回响应；如果没有与指定ID的连接，`404 Not Found`则返回响应。每个SSE事件代表从客户端到服务器的单个帧。传输使用未命名的事件，这意味着仅该`data`字段可用。因此，我们将`data`字段的第一行用于帧元数据。

服务器发送事件传输仅支持文本数据，因为它是基于文本的协议。结果，服务器将其报告为仅支持`Text`传输格式。如果客户端希望发送任意二进制数据，则在选择适当的传输时应跳过“服务器发送事件”传输。

客户端完成连接后，可以终止事件流连接（发送TCP重置）。服务器将清理必要的资源。

## 长轮询（仅服务器到客户端）

长轮询是服务器到客户端的半传输，因此它总是与HTTP Post配对。它要求已经使用该`POST [endpoint-base]/negotiate`请求建立了连接。

长轮询要求客户端轮询服务器以查找新消息。与传统轮询不同，如果没有可用数据，则服务器将仅等待消息发送。在某个时候，服务器，客户端或上游代理可能会终止连接，此时客户端应立即重新发送请求。长轮询是唯一允许“重新连接”的传输，当服务器认为现有请求正在处理时，可以在其中接收新请求。由于超时，可能会发生这种情况。发生这种情况时，现有请求将立即以状态码终止`204 No Content`。已写入现有请求的所有消息都将被刷新并视为已发送。如果服务器端超时而没有数据，则a`200 OK`为0`Content-Length` 将被发送，客户端应再次轮询以获取更多数据。

通过`[endpoint-base]`使用以下查询字符串参数将HTTP GET请求发送到来建立轮询

#### 版本1

- `id` （必需）-目标连接的连接令牌。

#### 版本0

- `id` （必需）-目标连接的连接ID。

当有可用数据时，服务器将以以下两种格式之一响应正文（取决于`Accept`标头的值）。根据HTTP规范的分块编码部分，响应可以分块。

如果`id`缺少该参数，`400 Bad Request`则返回响应。如果存在与指定的ID不连接`id`，一`404 Not Found`返回响应。

客户端完成连接后，可以`DELETE`向`[endpoint-base]`（发出`id`查询字符串中的）发出请求以优雅地终止连接。服务器将使用最新的轮询完成，`204`以表明它已关闭。