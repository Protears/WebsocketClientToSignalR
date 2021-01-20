using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebsocketClientToSignalR
{
    /// <summary>
    /// 一个根据signalR官方协议编写的websocketClient To SignalRServer简单示例
    /// 协议文档链接：<see cref="https://github.com/dotnet/aspnetcore/blob/master/src/SignalR/docs/specs/HubProtocol.md"/>
    /// </summary>
    public class Program
    {
        /// <summary>
        /// SignalR服务端地址
        /// 通过浏览器地址<see cref="http://127.0.0.1:5000/hub/chat"/>测试可得字符串：Connection ID required
        /// </summary>
        static readonly string BaseUrl = "ws://127.0.0.1:5000/hub/chat";
        public static async Task<int> Main(string[] args)
        {
            await RunWebSockets(BaseUrl);
            return 0;
        }
        private static async Task RunWebSockets(string url)
        {
            var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol("protocol1");//添加协议

            await ws.ConnectAsync(new Uri(url), CancellationToken.None);

            //指定协议为 json {"protocol":"json","version":1}
            await ws.SendAsync(new HandshakeRequest().InitProtocolJson().ConvertToArraySegment(), WebSocketMessageType.Text, true, CancellationToken.None);//发送握手包
            Console.WriteLine("Send HandshakeRequest success");

            Console.WriteLine($"Connected:{ws.State}");

            var sending = Task.Run(async () =>
            {
                string line = "";
                ArraySegment<byte> buffer = null;
                while (line != "0")
                {
                    line = Console.ReadLine();
                    switch (line)
                    {
                        case "1":
                            buffer = Add();
                            break;
                        case "2":
                            buffer = SingleResultFailure();
                            break;
                        case "3":
                            buffer = Batched();
                            break;
                        case "4":
                            buffer = Stream();
                            break;
                        case "5":
                            buffer = StreamFailure();
                            break;
                        case "6":
                            buffer = NonBlocking();
                            break;
                        case "7":
                            AddStream(ws);
                            buffer = null;
                            break;
                        default:
                            buffer = null;
                            break;
                    }
                    if (buffer != null)
                    {
                        await ws.SendAsync(buffer, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: CancellationToken.None);
                    }
                }
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            });

            var receiving = Receiving(ws);

            await Task.WhenAll(sending, receiving);
        }
        private static async Task Receiving(ClientWebSocket ws)
        {
            var buffer = new byte[2048];

            while (true)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    //去除分隔符 0x1e ，获取json格式消息体
                    string message = Encoding.UTF8.GetString(buffer.RemoveSeparator(), 0, result.Count - 1);

                    MessageType messageType = message.Deserialize<MessageType>();
                    switch (messageType.type)//判断收到的消息类型并进行相应处理
                    {
                        case MessageTypeEnum.Invocation:
                            InvocationRecive invocation = message.Deserialize<InvocationRecive>();
                            Console.WriteLine($"{invocation.invocationId ?? "非阻塞的"}-->{invocation.target}-->{invocation.arguments}");
                            break;
                        case MessageTypeEnum.StreamItem:
                            Console.WriteLine(message);
                            break;
                        case MessageTypeEnum.Completion:
                            CompletionServer completion = message.Deserialize<CompletionServer>();
                            Console.WriteLine($"{completion.invocationId} 调用结果：{completion.result} {completion.error}");
                            break;
                        case MessageTypeEnum.StreamInvocation:
                            break;
                        case MessageTypeEnum.CancelInvocation:
                            break;
                        case MessageTypeEnum.Ping:
                            Console.WriteLine($"心跳:{message}");
                            break;
                        case MessageTypeEnum.Close:
                            Close close = message.Deserialize<Close>();
                            Console.WriteLine($"断开链接：{close.error}，是否允许重连：{close.allowReconnect}");
                            break;
                        default:
                            HandshakeResponse response = message.Deserialize<HandshakeResponse>();
                            if (!string.IsNullOrEmpty(response.error))
                            {
                                Console.WriteLine($"链接断开：{response.error}");
                            }
                            else
                            {
                                Console.WriteLine($"服务端允许请求的指定协议{message}");
                            }
                            break;
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }
            }
        }

        private static ArraySegment<byte> Add()
        {
            //  Invocation { Id = 42, Target = "Add", Arguments = [ 40, 2 ] }
            return new Invocation("1    add", "Add", new int[] { 40, 2 }).ConvertToArraySegment();
        }

        private static ArraySegment<byte> Batched()
        {
            //  Invocation { Id = 42, Target = "Batched", Arguments = [ 5 ] }
            return new Invocation("3    Batched", "Batched", new int[] { 5 }).ConvertToArraySegment();
        }

        private static ArraySegment<byte> NonBlocking()
        {
            //  Invocation { Target = "NonBlocking", Arguments = [ "foo" ] }
            return new Invocation("6    NonBlocking", "NonBlocking", new int[] { 5 }).ConvertToArraySegment();
        }

        private static async void AddStream(ClientWebSocket ws)
        {
            string id = Guid.NewGuid().ToString("N");

            //  C->S: Invocation { Id = 42, Target = "AddStream", Arguments = [ ], StreamIds = [ 1 ] }
            var buffer = new Invocation("42", "AddStream", new string[] { }, new string[] { id }).ConvertToArraySegment();
            await ws.SendAsync(buffer, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: CancellationToken.None);

            //  C->S: StreamItem { Id = 1, Item = 1 }
            buffer = new StreamItem(id, 1).ConvertToArraySegment();
            await ws.SendAsync(buffer, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: CancellationToken.None);

            //  C->S: StreamItem { Id = 1, Item = 2 }
            buffer = new StreamItem(id, 2).ConvertToArraySegment();
            await ws.SendAsync(buffer, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: CancellationToken.None);

            //  C->S: StreamItem { Id = 1, Item = 3 }
            buffer = new StreamItem(id, 3).ConvertToArraySegment();
            await ws.SendAsync(buffer, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: CancellationToken.None);

            //  C->S: Completion { Id = 1 }
            buffer = new CompletionItem(id).ConvertToArraySegment();
            await ws.SendAsync(buffer, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: CancellationToken.None);
        }

        private static ArraySegment<byte> SingleResultFailure()
        {
            //  Invocation { Id = 42, Target = "SingleResultFailure", Arguments = [ 40, 2 ] }
            return new Invocation("2    SingleResultFailure", "SingleResultFailure", new int[] { 40, 2 }).ConvertToArraySegment();
        }
        private static ArraySegment<byte> Stream()
        {
            //  StreamInvocation { Id = 42, Target = "Stream", Arguments = [ 5 ] }
            return new StreamInvocation("4  Stream", "Stream", new int[] { 5 }).ConvertToArraySegment();
        }

        private static ArraySegment<byte> StreamFailure()
        {
            //  StreamInvocation { Id = 42, Target = "StreamFailure", Arguments = [ 5 ] }
            return new StreamInvocation("5  StreamFailure", "StreamFailure", new int[] { 5 }).ConvertToArraySegment();
        }
    }
    /// <summary>
    /// SignalR协议消息类型枚举
    /// </summary>
    public enum MessageTypeEnum : int
    {
        /// <summary>
        ///  指示请求以远程端点上提供的参数调用特定方法（目标）。
        /// </summary>
        Invocation = 1,
        /// <summary>
        /// 指示来自上一条StreamInvocation消息的流响应数据的单个项，或来自具有streamIds的调用的流上传的单个项
        /// </summary>
        StreamItem = 2,
        /// <summary>
        /// 表示先前Invocation或StreamInvocation已完成，或中的流Invocation或StreamInvocation已完成
        /// </summary>
        Completion = 3,
        /// <summary>
        /// 指示调用带有远程端点上提供的参数的流方法（目标）的请求
        /// </summary>
        StreamInvocation = 4,
        /// <summary>
        /// 客户端发送以取消服务器上的流调用
        /// </summary>
        CancelInvocation = 5,
        /// <summary>
        /// 由任何一方发送，以检查连接是否处于活动状态
        /// </summary>
        Ping = 6,
        /// <summary>
        /// 关闭连接后由服务器发送
        /// </summary>
        Close = 7
    }

    /// <summary>
    /// 客户端发送以取消服务器上的流调用。
    /// </summary>
    public struct CancelInvocation
    {
        /// <summary>
        /// 消息的可选String编码Invocation ID
        /// </summary>
        public string invocationId;

        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageTypeEnum type;
        public CancelInvocation(string invocationId)
        {
            type = MessageTypeEnum.CancelInvocation;
            this.invocationId = invocationId;
        }
    }

    /// <summary>
    /// 关闭连接后由服务器发送。如果由于错误而关闭了连接，则包含错误。
    /// </summary>
    public struct Close
    {
        /// <summary>
        /// 可选选项Boolean，用于向启用了自动重新连接的客户端指示，他们应在收到消息后尝试重新连接
        /// </summary>
        public bool allowReconnect;

        /// <summary>
        /// 可选的String编码错误消息
        /// </summary>
        public string error;

        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageTypeEnum type;
    }

    /// <summary>
    /// 用于标识StreamItem的结束，在流调用的情况下，StreamItem将不会再接收任何消息。
    /// </summary>
    public struct CompletionItem
    {
        /// <summary>
        /// 消息的String编码Invocation ID
        /// </summary>
        public string invocationId;

        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageTypeEnum type;
        public CompletionItem(string invocationId)
        {
            type = MessageTypeEnum.Completion;
            this.invocationId = invocationId;
        }
    }

    /// <summary>
    /// 表示先前Invocation或StreamInvocation已完成，或中的流Invocation或StreamInvocation已完成。
    /// 如果调用以错误结束或非流方法调用的结果结束，则包含错误。void方法将不存在结果。
    /// </summary>
    public struct CompletionServer
    {
        /// <summary>
        /// （选填字段）编码错误消息
        /// </summary>
        public string error;

        /// <summary>
        /// 消息的String编码Invocation ID
        /// </summary>
        public string invocationId;

        /// <summary>
        /// （选填字段）Token对结果值进行编码
        /// </summary>
        public object result;

        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageTypeEnum type;
    }

    /// <summary>
    /// 客户发送，请求服务端以同意消息格式。
    /// </summary>
    public struct HandshakeRequest
    {
        /// <summary>
        /// 协议类型，json | messagepack
        /// </summary>
        public string protocol;

        /// <summary>
        /// 协议版本，指定为 1
        /// </summary>
        public int version;

        public HandshakeRequest InitProtocolJson()
        {
            protocol = "json";
            version = 1;
            return this;
        }
        public HandshakeRequest InitProtocolMessagePack()
        {
            protocol = "messagepack";
            version = 1;
            return this;
        }
    }

    /// <summary>
    /// 由服务器发送，作为对先前HandshakeRequest消息的确认。如果握手失败，则包含错误
    /// </summary>
    public struct HandshakeResponse
    {
        /// <summary>
        /// （选填字段）错误信息
        /// </summary>
        public string error;
    }
    public struct InvocationRecive
    {
        /// <summary>
        /// Array包含应用于Target中引用的方法的参数。这是JSON Token的序列，按照“JSON有效负载编码”部分中的指示进行编码
        /// </summary>
        public object arguments;

        /// <summary>
        /// （选填字段）JSON标头编码，标头作为带有字符串键和字符串值的字典进行传输
        /// </summary>
        public Dictionary<string, string> headers;

        /// <summary>
        /// （选填字段）消息的可选String编码Invocation ID，如果Invocation ID为空，这表明调用是“非阻塞的”，因此调用者不希望响应
        /// </summary>
        public string invocationId;

        /// <summary>
        /// （选填字段）代表从调用者到被调用者的流的唯一ID，并由Target中引用的方法使用
        /// </summary>
        public object streamIds;

        /// <summary>
        /// String编码Target名称，如被调用方的活页夹所期望的
        /// </summary>
        public string target;

        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageTypeEnum type;
    }
    /// <summary>
    /// 指示请求以远程端点上提供的参数调用特定方法（目标）。
    /// 以UTF-8编码，以ASCII字符0x1E（记录分隔符）终止
    /// </summary>
    public struct Invocation
    {
        /// <summary>
        /// Array包含应用于Target中引用的方法的参数。这是JSON Token的序列，按照“JSON有效负载编码”部分中的指示进行编码
        /// </summary>
        public Array arguments;

        /// <summary>
        /// （选填字段）JSON标头编码，标头作为带有字符串键和字符串值的字典进行传输
        /// </summary>
        public Dictionary<string, string> headers;

        /// <summary>
        /// （选填字段）消息的可选String编码Invocation ID，如果Invocation ID为空，这表明调用是“非阻塞的”，因此调用者不希望响应
        /// </summary>
        public string invocationId;

        /// <summary>
        /// （选填字段）代表从调用者到被调用者的流的唯一ID，并由Target中引用的方法使用
        /// </summary>
        public Array streamIds;

        /// <summary>
        /// String编码Target名称，如被调用方的活页夹所期望的
        /// </summary>
        public string target;

        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageTypeEnum type;
        public Invocation(string invocationId, string target, Array arguments, Array streamIds = null, Dictionary<string, string> headers = null)
        {
            type = MessageTypeEnum.Invocation;
            this.invocationId = invocationId;
            this.target = target;
            this.arguments = arguments;
            this.streamIds = streamIds ?? (new int[] { });
            this.headers = headers ?? new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// 接收的消息类型
    /// </summary>
    public struct MessageType
    {
        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageTypeEnum type;
    }

    /// <summary>
    /// 由任何一方发送，以检查连接是否处于活动状态。
    /// </summary>
    public struct Ping
    {
        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageTypeEnum type;
        public Ping Init()
        {
            type = MessageTypeEnum.Ping;
            return this;
        }
    }

    /// <summary>
    /// StreamInvocation 指示调用带有远程端点上提供的参数的流方法（目标）的请求。
    /// </summary>
    public struct StreamInvocation
    {
        /// <summary>
        /// Array包含应用于Target中引用的方法的参数。这是JSON Token的序列，按照“ JSON有效负载编码”部分中的指示进行编码
        /// </summary>
        public Array arguments;

        /// <summary>
        /// （选填字段）JSON标头编码，标头作为带有字符串键和字符串值的字典进行传输
        /// </summary>
        public Dictionary<string, string> headers;

        /// <summary>
        /// （选填字段）消息的可选String编码Invocation ID，如果Invocation ID为空，这表明调用是“非阻塞的”，因此调用者不希望响应
        /// </summary>
        public string invocationId;

        /// <summary>
        /// （选填字段）代表从调用者到被调用者的流的唯一ID，并由Target中引用的方法使用
        /// </summary>
        public Array streamIds;

        /// <summary>
        /// String编码Target名称，如被调用方的活页夹所期望的
        /// </summary>
        public string target;

        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageTypeEnum type;
        public StreamInvocation(string invocationId, string target, Array arguments, Array streamIds = null, Dictionary<string, string> headers = null)
        {
            type = MessageTypeEnum.StreamInvocation;
            this.invocationId = invocationId;
            this.target = target;
            this.arguments = arguments;
            this.streamIds = streamIds ?? (new int[] { });
            this.headers = headers ?? new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// 指示来自上StreamInvocation一条消息的流响应数据的单个项，或来自具有streamIds的调用的流上传的单个项
    /// </summary>
    public struct StreamItem
    {
        /// <summary>
        /// （选填字段）消息的String编码Invocation ID，如果Invocation ID为空，这表明调用是“非阻塞的”，因此调用者不希望响应
        /// </summary>
        public string invocationId;

        /// <summary>
        /// Token对流项目进行编码
        /// </summary>
        public object item;

        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageTypeEnum type;
        public StreamItem(string invocationId, object item)
        {
            type = MessageTypeEnum.StreamItem;
            this.invocationId = invocationId;
            this.item = item;
        }
    }
    /// <summary>
    /// 扩展方法
    /// </summary>
    public static class Extentions
    {
        /// <summary>
        /// 添加 0x1e 分隔符
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] AddSeparator(this byte[] data)
        {
            List<byte> t = new List<byte>(data) { 0x1e };//0x1e record separator
            return t.ToArray();
        }

        /// <summary>
        /// 移除 0x1e 分隔符
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] RemoveSeparator(this byte[] data)
        {
            List<byte> t = new List<byte>(data);
            t.Remove(0x1e);
            return t.ToArray();
        }

        /// <summary>
        /// 类型转json字符串，以UTF8编码转byte数组，加分隔符0x1e后再转为ArraySegment<byte>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ArraySegment<byte> ConvertToArraySegment<T>(this T value)
        {
            string strJson = JsonConvert.SerializeObject(value);
            var bytes = Encoding.UTF8.GetBytes(strJson).AddSeparator();
            return new ArraySegment<byte>(bytes);
        }

        /// <summary>
        /// json字符串转实体
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public static T Deserialize<T>(this string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
    }

}
