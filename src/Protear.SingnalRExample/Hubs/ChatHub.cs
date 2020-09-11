using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Protear.SingnalRExample
{
    public class ChatHub : Hub
    {
        public ChatHub()
        {
        }
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

        private readonly List<string> _callers = new List<string>();
        public void NonBlocking(string caller)
        {
            _callers.Add(caller);
            Clients.All.SendAsync("ServerTest", "测试主动推送数据", CancellationToken.None).ConfigureAwait(false);
        }

        public async Task<int> AddStream(IAsyncEnumerable<int> stream)
        {
            int sum = 0;
            await foreach (var item in stream)
            {
                Console.WriteLine($"{sum}、{item}");
                sum += item;
            }
            return sum;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            await base.OnDisconnectedAsync(exception);
        }
    }
}
