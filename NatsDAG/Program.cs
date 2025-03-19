using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using NATS.Client.Core;

class DAGNode
{
    private readonly string nodeName;
    private readonly List<string> nextNodes;
    private readonly NatsConnection natsConnection;
    private readonly Dictionary<string, int> dependencyCounts;
    private readonly Channel<string> dependencyChannel;

    public DAGNode(string nodeName, List<string> nextNodes, NatsConnection connection, Dictionary<string, int> dependencyCounts = null)
    {
        this.nodeName = nodeName;
        this.nextNodes = nextNodes;
        this.natsConnection = connection;
        this.dependencyCounts = dependencyCounts;

        if (dependencyCounts != null && dependencyCounts.ContainsKey(nodeName))
        {
            // 非同期キュー（Channel）を作成
            dependencyChannel = Channel.CreateUnbounded<string>();
        }
    }

    public async Task StartAsync()
    {
        await foreach (var msg in natsConnection.SubscribeAsync<byte[]>(nodeName))
        {
            string receivedMessage = Encoding.UTF8.GetString(msg.Data);
            Console.WriteLine($"[Node {nodeName}] Received: {receivedMessage}");

            // ノードの処理（シミュレーション）
            await Task.Delay(500);

            if (dependencyChannel != null)
            {
                // 依存関係の数だけメッセージをキューに入れる
                await dependencyChannel.Writer.WriteAsync(receivedMessage);

                // まだ必要な数のメッセージを受信していなければ待機
                if (dependencyChannel.Reader.Count < dependencyCounts[nodeName])
                {
                    Console.WriteLine($"[Node {nodeName}] Waiting for {dependencyCounts[nodeName] - dependencyChannel.Reader.Count} more dependencies.");
                    continue;
                }

                // すべての依存関係を受信したら処理を開始
                for (int i = 0; i < dependencyCounts[nodeName]; i++)
                {
                    _ = await dependencyChannel.Reader.ReadAsync();
                }

                Console.WriteLine($"[Node {nodeName}] All dependencies met, processing...");
            }

            // 次のノードへメッセージを送信
            foreach (var nextNode in nextNodes)
            {
                var message = $"Processed by {nodeName}";
                await natsConnection.PublishAsync(nextNode, Encoding.UTF8.GetBytes(message));
                Console.WriteLine($"[Node {nodeName}] Sent to {nextNode}: {message}");
            }
        }
    }
}

class Program
{
    static async Task Main()
    {
        await using var connection = new NatsConnection(new NatsOpts { Url = "nats://localhost:4222" });

        // DAG の依存関係のカウント（D は B と C の 2 つのメッセージを待つ, E は D を待つ）
        var dependencyCounts = new Dictionary<string, int>
        {
            { "D", 2 }, // D は B と C の 2 つの処理完了を待つ
            { "E", 1 }  // E は D の処理完了を待つ
        };

        // DAG ノードの作成
        var dagNodes = new Dictionary<string, DAGNode>
        {
            { "A", new DAGNode("A", new List<string> { "B", "C" }, connection) },
            { "B", new DAGNode("B", new List<string> { "D" }, connection) },
            { "C", new DAGNode("C", new List<string> { "D" }, connection) },
            { "D", new DAGNode("D", new List<string> { "E" }, connection, dependencyCounts) }, // 依存関係あり
            { "E", new DAGNode("E", new List<string>(), connection, dependencyCounts) } // 依存関係あり
        };

        // 各ノードを非同期で実行
        var tasks = new List<Task>();
        foreach (var node in dagNodes.Values)
        {
            tasks.Add(node.StartAsync());
        }

        // 初期メッセージをノードAに送信
        await connection.PublishAsync("A", Encoding.UTF8.GetBytes("Start DAG"));

        Console.WriteLine("DAG Execution Started. Press Enter to exit.");
        Console.ReadLine();
    }
}
